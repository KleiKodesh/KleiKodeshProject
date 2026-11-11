using System.Collections.Generic;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// Merges a book's "Seifim" alt structure into its main TOC as one more level: each
    /// seif under its siman. Only called once SeforimDbSqlStrings.IsSeifimStructureMergeable
    /// has proven the whole structure fits (all or nothing).
    /// </summary>
    internal static class SeifimTocMerge
    {
        /// <summary>Added to an alt entry's id to give a merged seif its main-TOC id: clear of
        /// every tocEntry id, and stable, so a saved seif id stays valid.</summary>
        public const int MergedAltEntryIdOffset = 1_000_000_000;

        /// <summary>
        /// Each seif goes right after its siman (and after the seifim before it), as the siman's
        /// child one level down. The SQL check has already proven every siman has its main-TOC
        /// twin (same line, same text, a leaf) - a siman without one here means the data changed
        /// between the two reads, and the main TOC is returned untouched.
        /// </summary>
        public static List<TocEntryRow> Merge(List<TocEntryRow> main, List<TocEntryRow> seifim, int structureId)
        {
            var mainLeafByLine = new Dictionary<int, TocEntryRow>();
            foreach (var m in main)
                if (m.HasChildren == 0 && m.LineId is int lineId) mainLeafByLine[lineId] = m;

            var seifimByParent = new Dictionary<int, List<TocEntryRow>>();
            foreach (var s in seifim)
                if (s.ParentId is int parentId)
                {
                    if (!seifimByParent.TryGetValue(parentId, out var kids)) seifimByParent[parentId] = kids = new List<TocEntryRow>();
                    kids.Add(s);
                }

            var childrenByMainId = new Dictionary<int, List<TocEntryRow>>();
            foreach (var siman in seifim)
            {
                if (!seifimByParent.TryGetValue(siman.Id, out var kids)) continue;
                if (siman.LineId is not int lineId || !mainLeafByLine.TryGetValue(lineId, out var twin) || twin.Text != siman.Text)
                    return main;
                childrenByMainId[twin.Id] = kids;
            }

            var merged = new List<TocEntryRow>(main.Count + seifim.Count);
            foreach (var m in main)
            {
                if (!childrenByMainId.TryGetValue(m.Id, out var kids))
                {
                    merged.Add(m);
                    continue;
                }
                m.HasChildren = 1;
                merged.Add(m);
                foreach (var k in kids)
                    merged.Add(new TocEntryRow
                    {
                        Id = MergedAltEntryIdOffset + k.Id,
                        ParentId = m.Id,
                        Level = m.Level + 1,
                        LineId = k.LineId,
                        HasChildren = 0,
                        Text = k.Text,
                        LineIndex = k.LineIndex,
                        AltStructureId = structureId,
                    });
            }
            return merged;
        }
    }
}
