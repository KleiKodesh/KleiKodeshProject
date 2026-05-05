using System.IO;
using System.Text;

namespace FtsLib.Core
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

            int    termLen     = _br.ReadInt32();
            byte[] termBytes   = _br.ReadBytes(termLen);
            int    chunkLen    = _br.ReadInt32();
            int    count       = _br.ReadInt32();
            uint   lastEncoded = _br.ReadUInt32();
            byte[] chunk       = _br.ReadBytes(chunkLen);

            CurrentTerm        = Encoding.UTF8.GetString(termBytes);
            CurrentChunk       = chunk;
            CurrentChunkLen    = chunkLen;
            CurrentCount       = count;
            CurrentLastEncoded = lastEncoded;
            return true;
        }

        public void Dispose() { _br?.Dispose(); _fs?.Dispose(); }
    }
}
