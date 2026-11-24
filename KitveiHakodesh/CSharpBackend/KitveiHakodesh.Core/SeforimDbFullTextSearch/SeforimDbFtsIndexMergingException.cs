using System;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch
{
    /// <summary>
    /// A search was refused because the index is mid-merge and briefly cannot answer.
    ///
    /// Core's own type, so a host can report "still building" without referencing the engine
    /// library. Transient by nature: the same query succeeds once the merge commits, which is
    /// why a host shows a wait state rather than an error.
    /// </summary>
    public sealed class SeforimDbFtsIndexMergingException : Exception
    {
        public SeforimDbFtsIndexMergingException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
