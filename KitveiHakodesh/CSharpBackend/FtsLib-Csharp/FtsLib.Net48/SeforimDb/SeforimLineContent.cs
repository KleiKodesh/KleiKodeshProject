using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using ZstdSharp;

namespace FtsLib.SeforimDb
{
    /// <summary>
    /// Reading line text out of a seforim database, whichever shape it is.
    ///
    /// Three layouts are in the wild and the indexer must read all of them:
    ///
    ///   Zayit, and Otzaria before schema 6
    ///       `line.content` holds plain text.
    ///   Otzaria schema 6
    ///       `line` has NO content column. The text lives in `line_content`, keyed by the
    ///       same id, and each value is a zstd frame built with a dictionary that ships
    ///       inside the database in `zstd_dict`.
    ///
    /// WHY THIS IS DUPLICATED FROM Core: KitveiHakodesh.Core references FtsLib, so FtsLib
    /// cannot reference Core back. This file is deliberately standalone and depends on
    /// nothing but ZstdSharp and System.Data.Common, which also lets the net48 and net10
    /// copies of FtsLib share it despite using different SQLite providers.
    ///
    /// It is public rather than internal because KitveiHakodeshService reads the same
    /// databases and references FtsLib but NOT Core - sharing this is better than a third
    /// copy of the same logic drifting on its own.
    ///
    /// NOT THREAD-SAFE: the decompressor holds a reusable buffer. One instance per
    /// connection, which is how ZayitDb uses it.
    /// </summary>
    public sealed class SeforimLineContent : IDisposable
    {
        /// <summary>Guards against a corrupt frame header asking for an absurd allocation.
        /// The largest real line in the library is about 1.3 MB.</summary>
        private const int MaximumLineBytes = 16 * 1024 * 1024;

        private readonly Dictionary<uint, Decompressor> _decompressorsByDictionaryId;

        private SeforimLineContent(
            bool hasSplitContent,
            Dictionary<uint, Decompressor> decompressorsByDictionaryId)
        {
            HasSplitContent = hasSplitContent;
            _decompressorsByDictionaryId = decompressorsByDictionaryId;
        }

        /// <summary>
        /// True when the text lives in `line_content` and `line` has no `content` column, so
        /// every query must join the sidecar instead of selecting the column.
        /// </summary>
        public bool HasSplitContent { get; }

        /// <summary>True when stored values are compressed frames rather than text.</summary>
        public bool IsCompressed => _decompressorsByDictionaryId != null;

        /// <summary>Probes an open connection and loads its dictionaries, if it has any.</summary>
        public static SeforimLineContent ForConnection(DbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            bool hasSplitContent =
                TableExists(connection, "line_content") &&
                !ColumnExists(connection, "line", "content");

            if (!TableExists(connection, "zstd_dict"))
                return new SeforimLineContent(hasSplitContent, null);

            var decompressorsByDictionaryId = new Dictionary<uint, Decompressor>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT dict FROM zstd_dict";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.IsDBNull(0)) continue;
                        var dictionaryBytes = (byte[])reader.GetValue(0);

                        var decompressor = new Decompressor();
                        decompressor.LoadDictionary(dictionaryBytes);
                        // Frames name the id zstd stamped into the dictionary, not the row's
                        // `id` column.
                        decompressorsByDictionaryId[ReadDictionaryId(dictionaryBytes)] = decompressor;
                    }
                }
            }

            if (decompressorsByDictionaryId.Count == 0)
                throw new InvalidOperationException(
                    "The seforim database has a zstd_dict table but no dictionaries in it, so "
                    + "its compressed line content cannot be read.");

            return new SeforimLineContent(hasSplitContent, decompressorsByDictionaryId);
        }

        // ── SQL fragments ──────────────────────────────────────────────
        // The text expression and the join that makes it resolvable. Every statement that
        // reads line text is built from these, so it exists once rather than once per layout.

        /// <summary>The text expression for a line, aliased `content`.</summary>
        public string ContentColumn(string lineAlias)
            => HasSplitContent
                ? "COALESCE(" + lineAlias + "_content.content, '') AS content"
                : lineAlias + ".content";

        /// <summary>The same expression with no alias, for a scalar read.</summary>
        public string ContentExpression(string lineAlias)
            => HasSplitContent
                ? "COALESCE(" + lineAlias + "_content.content, '')"
                : lineAlias + ".content";

        /// <summary>
        /// The join that brings the text into scope, or nothing when it is already a column
        /// of `line`. LEFT JOIN so a line with no sidecar row still comes back; the join is
        /// on the sidecar's INTEGER PRIMARY KEY, which is a rowid lookup.
        /// </summary>
        public string ContentJoin(string lineAlias)
            => HasSplitContent
                ? " LEFT JOIN line_content " + lineAlias + "_content"
                  + " ON " + lineAlias + "_content.id = " + lineAlias + ".id"
                : string.Empty;

        // ── Decoding ───────────────────────────────────────────────────

        /// <summary>Column <paramref name="ordinal"/> as text; empty when NULL.</summary>
        public string Read(DbDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return string.Empty;
            return Decode(reader.GetValue(ordinal)) ?? string.Empty;
        }

        /// <summary>A stored content value as text, or null when it is NULL.</summary>
        public string Decode(object value)
        {
            if (value == null || value is DBNull) return null;

            // Dispatch on the RUNTIME type: schema 6 declares the column TEXT and stores
            // blobs in it, so the declared type is no guide.
            var text = value as string;
            if (text != null) return text;

            var frame = value as byte[];
            if (frame == null)
                throw new InvalidOperationException(
                    "A line's content is " + value.GetType().Name
                    + ", which is neither text nor a compressed frame.");

            if (_decompressorsByDictionaryId == null)
                throw new InvalidOperationException(
                    "A line holds compressed content but the database has no zstd_dict table.");

            return DecodeFrame(frame);
        }

        private string DecodeFrame(byte[] frame)
        {
            uint dictionaryId = ReadFrameDictionaryId(frame);

            Decompressor decompressor;
            if (!_decompressorsByDictionaryId.TryGetValue(dictionaryId, out decompressor))
                throw new InvalidOperationException(
                    "A line was compressed with dictionary " + dictionaryId
                    + ", which is not in the database's zstd_dict table.");

            ulong declaredSize;
            try
            {
                declaredSize = Decompressor.GetDecompressedSize(frame);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "A line's content could not be read as a zstd frame: " + exception.Message);
            }

            if (declaredSize == 0) return string.Empty;
            if (declaredSize > MaximumLineBytes)
                throw new InvalidOperationException(
                    "A line declares a decompressed size of " + declaredSize + " bytes, beyond "
                    + "the " + MaximumLineBytes + "-byte limit for one line.");

            // The header's size is a capacity: exact for a one-shot frame, an over-estimate
            // for a streamed one. What was actually written is what counts.
            var destination = new byte[(int)declaredSize];
            int written = decompressor.Unwrap(frame, destination);

            if (written < 0 || written > destination.Length)
                throw new InvalidOperationException(
                    "A line decompressed to " + written + " bytes, which does not fit the "
                    + destination.Length + " its frame header allowed for.");

            return Encoding.UTF8.GetString(destination, 0, written);
        }

        /// <summary>The id in a dictionary's own header: magic 0xEC30A437, then the id.</summary>
        private static uint ReadDictionaryId(byte[] dictionaryBytes)
        {
            const uint DictionaryMagic = 0xEC30A437;

            if (dictionaryBytes == null || dictionaryBytes.Length < 8)
                throw new InvalidOperationException("A zstd_dict row is too short to be a dictionary.");

            uint magic = (uint)(dictionaryBytes[0] | (dictionaryBytes[1] << 8)
                              | (dictionaryBytes[2] << 16) | (dictionaryBytes[3] << 24));
            if (magic != DictionaryMagic)
                throw new InvalidOperationException("A zstd_dict row is not a zstd dictionary.");

            return (uint)(dictionaryBytes[4] | (dictionaryBytes[5] << 8)
                        | (dictionaryBytes[6] << 16) | (dictionaryBytes[7] << 24));
        }

        /// <summary>
        /// The dictionary id a frame was built with. After the 4-byte magic comes the Frame
        /// Header Descriptor, whose low two bits size the id field; a Window Descriptor byte
        /// sits between them unless the Single Segment flag (bit 5) is set.
        /// </summary>
        private static uint ReadFrameDictionaryId(byte[] frame)
        {
            const uint FrameMagic = 0xFD2FB528;

            if (frame.Length < 5)
                throw new InvalidOperationException("A line's content is too short to be a zstd frame.");

            uint magic = (uint)(frame[0] | (frame[1] << 8) | (frame[2] << 16) | (frame[3] << 24));
            if (magic != FrameMagic)
                throw new InvalidOperationException(
                    "A line's content is a blob but not a zstd frame.");

            int descriptor = frame[4];
            int width = descriptor & 0x03;
            if (width == 3) width = 4;
            if (width == 0)
                throw new InvalidOperationException(
                    "A line's frame names no dictionary, so it cannot be decoded.");

            int offset = (descriptor & 0x20) != 0 ? 5 : 6;
            if (frame.Length < offset + width)
                throw new InvalidOperationException(
                    "A line's frame header is truncated before its dictionary id.");

            uint dictionaryId = 0;
            for (int i = 0; i < width; i++) dictionaryId |= (uint)frame[offset + i] << (8 * i);
            return dictionaryId;
        }

        private static bool TableExists(DbConnection connection, string tableName)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @tableName LIMIT 1";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@tableName";
                parameter.Value = tableName;
                command.Parameters.Add(parameter);
                return command.ExecuteScalar() != null;
            }
        }

        private static bool ColumnExists(DbConnection connection, string tableName, string columnName)
        {
            using (var command = connection.CreateCommand())
            {
                // PRAGMA takes no parameters; the table name is always one of our literals.
                command.CommandText = "PRAGMA table_info(" + tableName + ")";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        if (string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
                            return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            if (_decompressorsByDictionaryId == null) return;
            foreach (var decompressor in _decompressorsByDictionaryId.Values) decompressor.Dispose();
            _decompressorsByDictionaryId.Clear();
        }
    }
}
