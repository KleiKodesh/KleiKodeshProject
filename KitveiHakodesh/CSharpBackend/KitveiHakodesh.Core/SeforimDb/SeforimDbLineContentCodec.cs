using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using ZstdSharp;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// Turns a raw `content` value read from the seforim database into text.
    ///
    /// From SeforimLibrary schema 6 on, line content is zstd-compressed with a PREBUILT
    /// DICTIONARY that ships inside the database itself (table `zstd_dict`). Older Otzaria
    /// databases, and every Zayit database, store the same column as plain text. One codec
    /// covers all of them, because it dispatches on the RUNTIME TYPE of the value rather
    /// than on the declared column type:
    ///
    ///     NULL    -> null          (the caller decides what absence means; see the note
    ///                               on version lines in SeforimDbSqlStrings)
    ///     string  -> itself        (legacy database, or a schema-6 column that happens
    ///                               to hold text)
    ///     byte[]  -> decompressed  (a zstd frame built with one of the database's
    ///                               dictionaries)
    ///
    /// WHY NOT SNIFF THE COLUMN TYPE: schema 6 declares `line_content.content` as TEXT and
    /// stores BLOBs in it. SQLite allows that - affinity is a hint, not a constraint - so a
    /// `PRAGMA table_info` check would report TEXT and be wrong. The value's own type is the
    /// only honest signal, and it is the same signal Otzaria itself branches on.
    ///
    /// WHY NOT SNIFF THE ZSTD MAGIC: a plain-text line could in principle begin with those
    /// four bytes, and a truncated frame would not. Type dispatch has neither failure mode.
    ///
    /// NOT THREAD-SAFE. The decompressor holds a reusable buffer, so each connection gets
    /// its own codec (see <see cref="SeforimDbSchema"/>). Sharing one across threads
    /// corrupts output rather than throwing.
    /// </summary>
    public sealed class SeforimDbLineContentCodec : IDisposable
    {
        /// <summary>
        /// The codec for a database that stores content as plain text: every value passes
        /// through untouched. Shared and immutable - it holds no decompression state.
        /// </summary>
        public static readonly SeforimDbLineContentCodec Plain = new SeforimDbLineContentCodec();

        /// <summary>
        /// Upper bound on a decompressed line, as a guard against a corrupt frame header
        /// asking us to allocate an absurd buffer. The largest real line in the library is
        /// around 1.3 MB, so 16 MB is far above anything legitimate while still bounded.
        /// Matches the limit Otzaria applies.
        /// </summary>
        private const int MaximumLineBytes = 16 * 1024 * 1024;

        /// <summary>
        /// One decompressor per dictionary, keyed by the dictionary id that zstd embeds in
        /// both the dictionary and every frame built from it. The schema permits several
        /// dictionaries; today the library ships exactly one (id 32768). Null on the plain
        /// codec.
        /// </summary>
        private readonly Dictionary<uint, Decompressor>? _decompressorsByDictionaryId;

        private bool _isDisposed;

        private SeforimDbLineContentCodec()
        {
            _decompressorsByDictionaryId = null;
        }

        private SeforimDbLineContentCodec(Dictionary<uint, Decompressor> decompressorsByDictionaryId)
        {
            _decompressorsByDictionaryId = decompressorsByDictionaryId;
        }

        /// <summary>True when this database compresses line content.</summary>
        public bool IsCompressed => _decompressorsByDictionaryId != null;

        /// <summary>
        /// Builds the codec for an open connection: loads every dictionary from `zstd_dict`,
        /// or returns <see cref="Plain"/> when the table is absent (legacy Otzaria, Zayit).
        /// </summary>
        public static SeforimDbLineContentCodec Create(DbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (!SeforimDbSchema.TableExists(connection, "zstd_dict")) return Plain;

            var decompressorsByDictionaryId = new Dictionary<uint, Decompressor>();
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT dict FROM zstd_dict";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (reader.IsDBNull(0)) continue;
                            var dictionaryBytes = (byte[])reader.GetValue(0);

                            // The id the frames reference is the one zstd stamped into the
                            // dictionary, NOT the table's `id` column - which is just a
                            // primary key and is never mentioned by a frame.
                            uint dictionaryId = ReadDictionaryId(dictionaryBytes);

                            var decompressor = new Decompressor();
                            decompressor.LoadDictionary(dictionaryBytes);
                            decompressorsByDictionaryId[dictionaryId] = decompressor;
                        }
                    }
                }
            }
            catch
            {
                foreach (var decompressor in decompressorsByDictionaryId.Values) decompressor.Dispose();
                throw;
            }

            // A `zstd_dict` table with no usable rows is a broken database, not a plain one:
            // the content column will still hold frames that nothing can decode.
            if (decompressorsByDictionaryId.Count == 0)
                throw new SeforimDbContentCodecException(
                    "The database has a zstd_dict table but no dictionaries in it, so its " +
                    "compressed line content cannot be decoded.");

            return new SeforimDbLineContentCodec(decompressorsByDictionaryId);
        }

        /// <summary>
        /// The text of a `content` value, or null when the value is NULL.
        ///
        /// Throws rather than guessing when a value cannot be decoded. A silent fallback here
        /// would surface as mojibake or as blank lines in the reader's text, which is far
        /// harder to trace back than an exception naming the database.
        /// </summary>
        public string? Decode(object? value)
        {
            if (value == null || value == DBNull.Value) return null;

            if (value is string text) return text;

            if (value is byte[] frame)
            {
                if (_decompressorsByDictionaryId == null)
                    throw new SeforimDbContentCodecException(
                        "A line holds compressed content but the database has no zstd_dict " +
                        "table, so no dictionary is available to decode it.");

                return DecodeFrame(frame);
            }

            throw new SeforimDbContentCodecException(
                "A line's content column holds " + value.GetType().Name +
                ", which is neither text nor a compressed frame.");
        }

        /// <summary>
        /// Reads column <paramref name="ordinal"/> as decoded text. The common path for query
        /// code, which would otherwise repeat the GetValue/Decode pair at every call site.
        /// </summary>
        public string? DecodeColumn(DbDataReader reader, int ordinal)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            return reader.IsDBNull(ordinal) ? null : Decode(reader.GetValue(ordinal));
        }

        /// <summary>
        /// Reads column <paramref name="ordinal"/> as decoded text, mapping NULL to empty.
        /// For base line content, where a missing row and an empty line read the same way.
        /// Version lines must NOT use this - see SeforimDbSqlStrings for why NULL there is
        /// a meaningful value rather than an absence.
        /// </summary>
        public string DecodeColumnOrEmpty(DbDataReader reader, int ordinal)
            => DecodeColumn(reader, ordinal) ?? string.Empty;

        /// <summary>Decodes one frame. Only reachable on a compressed codec.</summary>
        private string DecodeFrame(byte[] frame)
        {
            var decompressorsByDictionaryId = _decompressorsByDictionaryId
                ?? throw new SeforimDbContentCodecException(
                    "A line holds compressed content but no dictionary is loaded.");

            uint dictionaryId = ReadFrameDictionaryId(frame);

            if (!decompressorsByDictionaryId.TryGetValue(dictionaryId, out var decompressor))
                throw new SeforimDbContentCodecException(
                    "A line was compressed with dictionary " + dictionaryId +
                    ", which is not present in the database's zstd_dict table.");

            // The frame header's content size is a CAPACITY HINT, not a promise. For a frame
            // written in one shot - which is every frame the library ships - it is the exact
            // decompressed length. For a frame written as a stream it is an estimated window
            // size instead, which is LARGER than the real content. Treating it as exact would
            // reject such a frame as corrupt even though it decodes perfectly, so the number
            // of bytes actually written is what counts.
            ulong declaredSize;
            try
            {
                declaredSize = Decompressor.GetDecompressedSize(frame);
            }
            catch (Exception exception)
            {
                // A malformed frame throws out of the size read. Translate it, so a caller
                // only ever has to catch this file's own exception type.
                throw new SeforimDbContentCodecException(
                    "A line's compressed content could not be read as a zstd frame: " +
                    exception.Message);
            }

            if (declaredSize == 0) return string.Empty;
            if (declaredSize > MaximumLineBytes)
                throw new SeforimDbContentCodecException(
                    "A line declares a decompressed size of " + declaredSize +
                    " bytes, beyond the " + MaximumLineBytes + "-byte limit for a single line.");

            var destination = new byte[(int)declaredSize];

            int written;
            try
            {
                written = decompressor.Unwrap(frame, destination);
            }
            catch (Exception exception)
            {
                throw new SeforimDbContentCodecException(
                    "A line's compressed content could not be decompressed: " + exception.Message);
            }

            if (written < 0 || written > destination.Length)
                throw new SeforimDbContentCodecException(
                    "A line decompressed to " + written + " bytes, which does not fit the " +
                    destination.Length + " bytes its frame header allowed for.");

            return Encoding.UTF8.GetString(destination, 0, written);
        }

        /// <summary>
        /// The dictionary id stamped in a dictionary's own header: magic 0xEC30A437 followed
        /// by the id, both little-endian.
        /// </summary>
        private static uint ReadDictionaryId(byte[] dictionaryBytes)
        {
            const uint DictionaryMagic = 0xEC30A437;

            if (dictionaryBytes == null || dictionaryBytes.Length < 8)
                throw new SeforimDbContentCodecException(
                    "A row in zstd_dict is too short to be a zstd dictionary.");

            uint magic = (uint)(dictionaryBytes[0]
                              | (dictionaryBytes[1] << 8)
                              | (dictionaryBytes[2] << 16)
                              | (dictionaryBytes[3] << 24));

            if (magic != DictionaryMagic)
                throw new SeforimDbContentCodecException(
                    "A row in zstd_dict does not start with the zstd dictionary magic.");

            return (uint)(dictionaryBytes[4]
                        | (dictionaryBytes[5] << 8)
                        | (dictionaryBytes[6] << 16)
                        | (dictionaryBytes[7] << 24));
        }

        /// <summary>
        /// The dictionary id a frame was built with, read from the frame header.
        ///
        /// Layout: 4-byte magic 0xFD2FB528, then the Frame Header Descriptor, whose low two
        /// bits say how wide the dictionary id field is (0 = absent, 1 = one byte, 2 = two
        /// bytes, 3 = four bytes). The library's frames use the two-byte form.
        /// </summary>
        private static uint ReadFrameDictionaryId(byte[] frame)
        {
            const uint FrameMagic = 0xFD2FB528;

            if (frame.Length < 5)
                throw new SeforimDbContentCodecException(
                    "A line's compressed content is too short to be a zstd frame.");

            uint magic = (uint)(frame[0] | (frame[1] << 8) | (frame[2] << 16) | (frame[3] << 24));
            if (magic != FrameMagic)
                throw new SeforimDbContentCodecException(
                    "A line's content is a blob but does not start with the zstd frame magic.");

            int frameHeaderDescriptor = frame[4];

            int dictionaryIdFieldWidth = frameHeaderDescriptor & 0x03;
            if (dictionaryIdFieldWidth == 3) dictionaryIdFieldWidth = 4;

            if (dictionaryIdFieldWidth == 0)
                throw new SeforimDbContentCodecException(
                    "A line's frame carries no dictionary id, so the dictionary that built " +
                    "it cannot be identified.");

            // The dictionary id follows the descriptor, but a Window Descriptor byte sits
            // between them UNLESS the Single Segment flag (bit 5) is set. Every frame the
            // library ships today is single-segment, so this is always 5 in practice - but
            // reading the flag rather than assuming it costs nothing, and assuming it would
            // silently decode the wrong bytes as a dictionary id if that ever changed.
            bool isSingleSegment = (frameHeaderDescriptor & 0x20) != 0;
            int offset = isSingleSegment ? 5 : 6;
            if (frame.Length < offset + dictionaryIdFieldWidth)
                throw new SeforimDbContentCodecException(
                    "A line's frame header is truncated before its dictionary id.");

            uint dictionaryId = 0;
            for (int byteIndex = 0; byteIndex < dictionaryIdFieldWidth; byteIndex++)
                dictionaryId |= (uint)frame[offset + byteIndex] << (8 * byteIndex);

            return dictionaryId;
        }

        /// <summary>
        /// Releases this codec's dictionaries.
        ///
        /// Disposing the shared <see cref="Plain"/> codec does nothing, deliberately: callers
        /// wrap whatever <see cref="Create"/> hands them in a `using` without knowing which
        /// codec they got, and a legacy database gets the singleton. Marking the singleton
        /// disposed would let one `using` block on one connection poison every other legacy
        /// connection in the process.
        /// </summary>
        public void Dispose()
        {
            // The plain codec owns nothing and is shared process-wide - never mark it used up.
            if (_decompressorsByDictionaryId == null) return;
            if (_isDisposed) return;
            _isDisposed = true;
            foreach (var decompressor in _decompressorsByDictionaryId.Values) decompressor.Dispose();
            _decompressorsByDictionaryId.Clear();
        }
    }

    /// <summary>
    /// A line's stored content could not be turned into text. Always a problem with the
    /// database file or with which database was opened, never with the query.
    /// </summary>
    public sealed class SeforimDbContentCodecException : Exception
    {
        public SeforimDbContentCodecException(string message) : base(message) { }
    }
}
