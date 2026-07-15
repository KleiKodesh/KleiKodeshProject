using System.IO;
using System.Text;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Forward-only reader for a sorted segment file.
    /// Reads one term at a time in ascending term order.
    ///
    /// Segment record format (per term) — format v2:
    ///   varint   termByteLen
    ///   N bytes  term (UTF-8)
    ///   varint   chunkByteLen
    ///   varint   docCount
    ///   varint   lastEncoded    (no-offset: actual last doc id)
    ///   varint   skipCount
    ///   skipCount × 12 bytes  skip table (int32 docId, int32 byteOffset, int32 prevEncoded)
    ///   M bytes  varint posting data (delta, first value = actual doc id, no int.MinValue rebase)
    ///
    /// v2 vs v1: the five header scalars are varint (were fixed int32/uint32 = 20 B) and the
    /// posting stream drops the int.MinValue offset. The search read path uses the .db offsets
    /// and never parses this header; only the sequential merge reader does.
    /// </summary>
    internal sealed class SegmentReader : System.IDisposable
    {
        private readonly FileStream   _fs;
        private readonly BinaryReader _br;

        public string CurrentTerm        { get; private set; }
        public byte[] CurrentChunk       { get; private set; }
        public int    CurrentChunkLen    { get; private set; }
        public int    CurrentCount       { get; private set; }
        public uint   CurrentLastEncoded { get; private set; }
        /// <summary>
        /// Skip table for the current term, as a flat int[] triplets
        /// (docId, byteOffset, prevEncoded). Null when skipCount is 0.
        /// </summary>
        public int[]  CurrentSkip        { get; private set; }
        public int    CurrentSkipLen     { get; private set; }
        public bool   Done               { get; private set; }

        public SegmentReader(string path)
        {
            _fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                 FileShare.Read, bufferSize: 4 * 1024 * 1024);
            _br = new BinaryReader(_fs, Encoding.UTF8, leaveOpen: false);
        }

        public bool MoveNext()
        {
            if (Done || _fs.Position >= _fs.Length) { Done = true; return false; }

            // Format v2: the five header scalars are varint-encoded (see SegmentWriter).
            long recStart = _fs.Position;
            int termLen = (int)FtsLib.Search.VarInt.Read(_br);
            if (termLen < 0 || termLen > 4096)
                throw new InvalidDataException(
                    $"Corrupt segment: invalid termLen {termLen} at offset {recStart}");

            byte[] termBytes = _br.ReadBytes(termLen);

            int chunkLen = (int)FtsLib.Search.VarInt.Read(_br);
            if (chunkLen < 0 || chunkLen > 64 * 1024 * 1024)
                throw new InvalidDataException(
                    $"Corrupt segment: invalid chunkLen {chunkLen} at offset {recStart}");

            int    count       = (int)FtsLib.Search.VarInt.Read(_br);
            uint   lastEncoded =      FtsLib.Search.VarInt.Read(_br);
            int    skipCount   = (int)FtsLib.Search.VarInt.Read(_br);

            int[]  skip    = null;
            int    skipLen = skipCount * 3;
            if (skipCount > 0)
            {
                skip = new int[skipLen];
                for (int i = 0; i < skipLen; i++)
                    skip[i] = _br.ReadInt32();
            }

            byte[] chunk = _br.ReadBytes(chunkLen);

            CurrentTerm        = Encoding.UTF8.GetString(termBytes);
            CurrentChunk       = chunk;
            CurrentChunkLen    = chunkLen;
            CurrentCount       = count;
            CurrentLastEncoded = lastEncoded;
            CurrentSkip        = skip;
            CurrentSkipLen     = skipLen;
            return true;
        }

        public void Dispose() { _br?.Dispose(); _fs?.Dispose(); }
    }
}
