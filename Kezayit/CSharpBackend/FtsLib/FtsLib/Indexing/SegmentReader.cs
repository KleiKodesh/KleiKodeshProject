using System.IO;
using System.Text;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Forward-only reader for a sorted segment file.
    /// Reads one term at a time in ascending term order.
    ///
    /// Segment record format (per term):
    ///   4 bytes  int    termByteLen
    ///   N bytes         term (UTF-8)
    ///   4 bytes  int    chunkByteLen
    ///   4 bytes  int    docCount
    ///   4 bytes  uint   lastEncoded
    ///   4 bytes  int    skipCount
    ///   skipCount × 12 bytes  skip table (int32 docId, int32 byteOffset, int32 prevEncoded)
    ///   M bytes         varint posting data
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

            int termLen = _br.ReadInt32();
            if (termLen < 0 || termLen > 4096)
                throw new InvalidDataException(
                    $"Corrupt segment: invalid termLen {termLen} at offset {_fs.Position - 4}");

            byte[] termBytes = _br.ReadBytes(termLen);

            int chunkLen = _br.ReadInt32();
            if (chunkLen < 0 || chunkLen > 64 * 1024 * 1024)
                throw new InvalidDataException(
                    $"Corrupt segment: invalid chunkLen {chunkLen} at offset {_fs.Position - 4}");

            int    count       = _br.ReadInt32();
            uint   lastEncoded = _br.ReadUInt32();
            int    skipCount   = _br.ReadInt32();

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
