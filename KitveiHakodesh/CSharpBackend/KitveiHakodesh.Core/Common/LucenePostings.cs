#nullable enable
using System.Collections.Generic;
using Lucene.Net.Index;
using Lucene.Net.Util;

namespace KitveiHakodesh.Core.Common;

/// <summary>
/// Per-segment postings cursors for a collector that reads a doc's terms while collecting:
/// one enum per term, advanced forward in doc order (so the collector must collect in order).
/// Shared by the catalog and the local-file rank collectors.
/// </summary>
internal static class LucenePostings
{
    /// <summary>One postings enum per term of <paramref name="terms"/> in <paramref name="field"/>
    /// (null where the segment lacks the term). With <paramref name="positions"/>, a
    /// <see cref="DocsAndPositionsEnum"/> when the field has positions, else a frequencies enum.</summary>
    public static DocsEnum?[] Open(AtomicReader reader, string field, IReadOnlyList<string> terms, bool positions)
    {
        var enums = new DocsEnum?[terms.Count];
        var fieldTerms = reader.GetTerms(field);
        if (fieldTerms is null) return enums;
        var te = fieldTerms.GetEnumerator();
        for (int i = 0; i < terms.Count; i++)
        {
            if (!te.SeekExact(new BytesRef(terms[i]))) continue;
            enums[i] = positions && fieldTerms.HasPositions
                ? te.DocsAndPositions(reader.LiveDocs, null, DocsAndPositionsFlags.NONE)
                : te.Docs(reader.LiveDocs, null, positions ? DocsFlags.FREQS : DocsFlags.NONE);
        }
        return enums;
    }

    /// <summary>Advance <paramref name="e"/> to <paramref name="doc"/> (never backwards); true
    /// when it holds the doc.</summary>
    public static bool At(DocsEnum? e, int doc)
    {
        if (e is null) return false;
        if (e.DocID < doc) e.Advance(doc);
        return e.DocID == doc;
    }

    /// <summary>The doc's frequency and, when <paramref name="e"/> carries them, its positions
    /// (appended to <paramref name="positions"/>, which is cleared first). 0 when the doc is
    /// not in the enum.</summary>
    public static int FreqAndPositions(DocsEnum? e, int doc, List<int> positions)
    {
        positions.Clear();
        if (!At(e, doc)) return 0;
        int freq = e!.Freq;
        if (e is DocsAndPositionsEnum p)
            for (int k = 0; k < freq; k++) positions.Add(p.NextPosition());
        return freq;
    }
}
