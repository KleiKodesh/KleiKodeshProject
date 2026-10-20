namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// Keeps row ids unique across the primary seforim.db and every library ATTACHed alongside
    /// it (see <see cref="SeforimDbAttachedLibraries"/>), by giving each library's local ids a
    /// private, non-overlapping band of the int32 range — rather than inventing a composite key
    /// type that would ripple through every wire model, every query's WHERE clause, and the
    /// frontend state that stores a book id today.
    ///
    /// Library 0 — the primary seforim.db, SQLite schema "main" — keeps its ids EXACTLY as the
    /// file stores them. Nothing is remapped, so every id already in use before this feature
    /// existed (bookmarks, reading history, annotations, deep links) keeps meaning what it
    /// always meant. Library N (N >= 1, schema "lib{N}") has its local ids shifted up by
    /// N * <see cref="OffsetUnit"/>.
    ///
    /// OffsetUnit is deliberately generous rather than tight: a real seforim.db has, at the time
    /// of writing, on the order of a few million rows in its largest table (line). 50 million
    /// leaves well over an order of magnitude of headroom before a local id could collide with
    /// the next library's band — and <see cref="SeforimDbAttachedLibraries"/> refuses to attach
    /// a library whose own ids do not actually fit, rather than trusting this headroom blindly —
    /// while still leaving room for <see cref="MaxLibraryIndex"/> distinct library SLOTS inside
    /// int32.
    ///
    /// A "slot" here is a PERSISTED index assigned once, by
    /// <see cref="KitveiHakodesh.Core.Settings.SeforimDbAdditionalLibraries"/>, when a path is
    /// first added — never recomputed from the current list of configured or attached libraries.
    /// A library's index therefore never changes because an EARLIER library was removed or is
    /// temporarily unavailable (a disconnected network drive, say): if it did, every annotation
    /// and bookmark that embeds a global id built from that index would silently start pointing
    /// at a different library's book the next time the service restarted. Once assigned, an
    /// index is retired when its library is removed — never reused for a different path — which
    /// is why MaxLibraryIndex is a lifetime budget of slots for this install, not merely "how
    /// many libraries can be attached at once".
    /// </summary>
    internal static class SeforimDbLibraryId
    {
        public const int OffsetUnit = 50_000_000;

        /// <summary>The highest library index whose ENTIRE band (index * OffsetUnit .. index * OffsetUnit +
        /// OffsetUnit - 1) still fits in int32 - 41 for OffsetUnit = 50M. int.MaxValue / OffsetUnit (42) would
        /// allow a local id near the cap to overflow.</summary>
        public const int MaxLibraryIndex = (int.MaxValue - (OffsetUnit - 1)) / OffsetUnit; // 41

        /// <summary>Maps a library's own local id to the global id every caller sees.</summary>
        public static int ToGlobal(int libraryIndex, int localId) =>
            libraryIndex == 0 ? localId : libraryIndex * OffsetUnit + localId;

        /// <summary>
        /// Splits a global id back into the library that owns it and that library's local id.
        /// Index 0 means the primary database (schema "main"); any other index N means the
        /// library ATTACHed as "lib{N}" — which may or may not still be attached on the
        /// connection in hand, see <see cref="SeforimDbAttachedLibraries.Resolve"/>.
        /// </summary>
        public static (int LibraryIndex, int LocalId) Resolve(int globalId)
        {
            if (globalId < OffsetUnit) return (0, globalId);

            int index = globalId / OffsetUnit;
            return (index, globalId - index * OffsetUnit);
        }
    }
}
