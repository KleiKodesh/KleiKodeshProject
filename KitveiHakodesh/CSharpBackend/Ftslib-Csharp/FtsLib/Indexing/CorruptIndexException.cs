using System;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Thrown when a segment file is corrupt and cannot be recovered.
    /// The caller should delete the entire index directory and trigger a clean rebuild.
    /// </summary>
    public sealed class CorruptIndexException : Exception
    {
        public CorruptIndexException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
