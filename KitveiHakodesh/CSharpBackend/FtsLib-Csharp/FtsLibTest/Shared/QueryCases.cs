using System;

namespace FtsLibTest
{
    /// <summary>
    /// The query battery (literal, wildcard, fuzzy, OR, word-distance, ordered, edge cases)
    /// shared by the FTS5 benchmarks. Moved out of the old engine's PerformanceTest.
    /// </summary>
    internal static class QueryCases
    {
        // ── Test cases ────────────────────────────────────────────────
        // Each entry: (label, query, maxWordDistance, requireOrdered)
        // maxWordDistance = int.MaxValue means "no filter" (UI default is 10,
        // but for perf tests we want to see raw throughput).

        internal static readonly PerfCase[] Cases =
        {
            // ── Single-word literals ──────────────────────────────────
            new PerfCase("literal / single common word",
                "תורה"),

            new PerfCase("literal / single rare word",
                "שויתי"),

            new PerfCase("literal / single word (English)",
                "torah"),

            // ── Multi-word AND literals ───────────────────────────────
            new PerfCase("literal / 2-word AND",
                "כי ביצחק"),

            new PerfCase("literal / 2-word AND (common)",
                "תורה מצוה"),

            new PerfCase("literal / 3-word AND",
                "אברהם יצחק יעקב"),

            new PerfCase("literal / 4-word AND",
                "אבל בן אין לה"),

            new PerfCase("literal / 5-word AND (long phrase)",
                "וידבר משה כן אל בני"),

            new PerfCase("literal / 6-word AND (very long phrase)",
                "שויתי לנגדי תמיד כי מימיני בל"),

            // ── Zero-result queries ───────────────────────────────────
            new PerfCase("literal / zero results (nonexistent word)",
                "nonexistentword123xyz"),

            new PerfCase("literal / zero results (impossible AND)",
                "nonexistentword123 תורה"),

            // ── Wildcard searches ─────────────────────────────────────
            new PerfCase("wildcard / prefix (short anchor)",
                "תור*"),

            new PerfCase("wildcard / prefix (longer anchor)",
                "תורה*"),

            new PerfCase("wildcard / suffix",
                "*ישראל"),

            new PerfCase("wildcard / infix",
                "*אבר*"),

            new PerfCase("wildcard / prefix + AND literal",
                "משה* תורה"),

            new PerfCase("wildcard / suffix + AND literal",
                "*ישראל תורה"),

            new PerfCase("wildcard / high-cardinality prefix (very short anchor)",
                "בני*"),

            new PerfCase("wildcard / optional char (?)",
                "תור?ה"),

            new PerfCase("wildcard / multiple optional chars",
                "תו?ר?ה"),

            // ── Fuzzy searches ────────────────────────────────────────
            new PerfCase("fuzzy / distance 1 (default)",
                "יצחק~"),

            new PerfCase("fuzzy / distance 1 + AND literal",
                "כי יצחק~"),

            new PerfCase("fuzzy / distance 2",
                "יסראל~2"),

            new PerfCase("fuzzy / distance 2 + AND literal",
                "כי יסראל~2"),

            new PerfCase("fuzzy / distance 3",
                "ישראל~3"),

            new PerfCase("fuzzy / 3-letter word distance 1",
                "אנב~"),

            new PerfCase("fuzzy / common word distance 1",
                "תארה~"),

            new PerfCase("fuzzy / common word distance 1 + AND",
                "תארה~ מצוה"),

            // ── OR groups ─────────────────────────────────────────────
            new PerfCase("OR / two alternatives",
                "תורה | מצוה"),

            new PerfCase("OR / three alternatives",
                "אברהם | יצחק | יעקב"),

            new PerfCase("OR / OR group AND literal",
                "תורה | מצוה כי"),

            new PerfCase("OR / literal AND OR group",
                "כי תורה | מצוה"),

            new PerfCase("OR / wildcard in OR group",
                "תור* | מצוה"),

            new PerfCase("OR / fuzzy in OR group",
                "יצחק~ | יעקב"),

            new PerfCase("OR / mixed wildcard + fuzzy in OR group",
                "תור* | יצחק~"),

            new PerfCase("OR / chained: a | b | c AND d",
                "אברהם | יצחק | יעקב תורה"),

            // ── Word-distance filter ──────────────────────────────────
            new PerfCase("word-dist / tight window (maxDist=0, adjacent only)",
                "כי ביצחק",
                maxWordDistance: 0),

            new PerfCase("word-dist / narrow window (maxDist=2)",
                "כי ביצחק",
                maxWordDistance: 2),

            new PerfCase("word-dist / default window (maxDist=10)",
                "כי ביצחק",
                maxWordDistance: 10),

            new PerfCase("word-dist / wide window (maxDist=50)",
                "כי ביצחק",
                maxWordDistance: 50),

            new PerfCase("word-dist / no filter (maxDist=int.MaxValue)",
                "כי ביצחק",
                maxWordDistance: int.MaxValue),

            new PerfCase("word-dist / 3-word AND tight (maxDist=0)",
                "אברהם יצחק יעקב",
                maxWordDistance: 0),

            new PerfCase("word-dist / 3-word AND default (maxDist=10)",
                "אברהם יצחק יעקב",
                maxWordDistance: 10),

            // ── Ordered search ────────────────────────────────────────
            new PerfCase("ordered / 2-word ordered",
                "כי ביצחק",
                requireOrdered: true),

            new PerfCase("ordered / 3-word ordered",
                "אברהם יצחק יעקב",
                requireOrdered: true),

            new PerfCase("ordered / 5-word ordered",
                "וידבר משה כן אל בני",
                requireOrdered: true),

            new PerfCase("ordered / 2-word ordered + tight distance",
                "כי ביצחק",
                maxWordDistance: 2,
                requireOrdered: true),

            new PerfCase("ordered / fuzzy + ordered",
                "כי יצחק~",
                requireOrdered: true),

            new PerfCase("ordered / wildcard + ordered",
                "משה* תורה",
                requireOrdered: true),

            // ── SearchIds() — ID-only path ────────────────────────────
            new PerfCase("searchids / single word",
                "תורה",
                idsOnly: true),

            new PerfCase("searchids / 2-word AND",
                "כי ביצחק",
                idsOnly: true),

            new PerfCase("searchids / wildcard",
                "בני*",
                idsOnly: true),

            new PerfCase("searchids / fuzzy",
                "יצחק~",
                idsOnly: true),

            // ── High-cardinality (stress) ─────────────────────────────
            new PerfCase("stress / very common single word",
                "כי"),

            new PerfCase("stress / very common 2-word AND",
                "כי לא"),

            new PerfCase("stress / high-cardinality wildcard",
                "כ*"),

            // ── Edge cases ────────────────────────────────────────────
            new PerfCase("edge / nikud in query (stripped by parser)",
                "שָׁלוֹם"),

            new PerfCase("edge / leading pipe ignored",
                "| תורה"),

            new PerfCase("edge / trailing pipe ignored",
                "תורה |"),

            new PerfCase("edge / double pipe treated as one",
                "תורה || מצוה"),

            new PerfCase("edge / fuzzy + wildcard on same token (wildcard wins)",
                "תור*~"),

            new PerfCase("edge / single-char token (dropped by tokenizer)",
                "א"),

            new PerfCase("edge / query with only pipes",
                "| | |"),
        };

        internal sealed class PerfCase
        {
            public readonly string Label;
            public readonly string Query;
            public readonly int    MaxWordDistance;
            public readonly bool   RequireOrdered;
            public readonly bool   IdsOnly;

            public PerfCase(
                string label,
                string query,
                int    maxWordDistance = int.MaxValue,
                bool   requireOrdered  = false,
                bool   idsOnly         = false)
            {
                Label           = label;
                Query           = query;
                MaxWordDistance = maxWordDistance;
                RequireOrdered  = requireOrdered;
                IdsOnly         = idsOnly;
            }
        }
    }
}
