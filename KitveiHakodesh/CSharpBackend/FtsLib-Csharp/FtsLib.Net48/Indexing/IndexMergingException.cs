using System;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Thrown by <see cref="SegmentStore.GetLiveSegmentPaths"/> when a merge is
    /// currently in progress and the caller requested a non-blocking snapshot.
    /// The caller should surface this to the user as a temporary unavailability
    /// rather than retrying silently.
    /// </summary>
    public sealed class IndexMergingException : Exception
    {
        public IndexMergingException()
            : base("Index is currently merging segments — please try again in a moment.") { }
    }
}
