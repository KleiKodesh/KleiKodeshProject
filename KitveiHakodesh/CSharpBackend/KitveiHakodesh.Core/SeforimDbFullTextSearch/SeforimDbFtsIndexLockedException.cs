using System;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch
{
    /// <summary>
    /// The index's write lock is still held after the retries are exhausted — an unusually
    /// long-lived previous instance, or a genuinely concurrent writer.
    ///
    /// Core's own type rather than the engine's, so a host can handle this case without
    /// referencing the engine library. Whether to re-arm and try again later is host policy:
    /// it is not a corrupt index and not a failed build, just a build that could not start now.
    /// </summary>
    public sealed class SeforimDbFtsIndexLockedException : Exception
    {
        public SeforimDbFtsIndexLockedException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
