using System;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>Shared utilities used by both test suites.</summary>
    internal static class TestHelpers
    {
        // ── Tier definitions ──────────────────────────────────────────

        public static readonly (string Label, int Limit)[] Tiers =
        {
            ("500k", 500_000),
            ("1m",   1_000_000),
            ("3m",   3_000_000),
            ("full", 0),
        };

        /// <summary>
        /// Resolves a tier by label (case-insensitive).
        /// Returns (label, limit) or throws with a helpful message.
        /// </summary>
        public static (string Label, int Limit) ResolveTier(string label)
        {
            foreach (var t in Tiers)
                if (string.Equals(t.Label, label, StringComparison.OrdinalIgnoreCase))
                    return t;

            throw new ArgumentException(
                $"Unknown tier '{label}'. Valid: {string.Join(", ", Array.ConvertAll(Tiers, t => t.Label))}");
        }

        // ── Index path ────────────────────────────────────────────────

        public static string IndexDir(string tierLabel) =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"index_{tierLabel.ToLowerInvariant()}");

        // ── Text helpers ──────────────────────────────────────────────

        /// <summary>
        /// Strips HTML tags and Hebrew diacritics so the validator sees the
        /// same normalised text the tokenizer produces.
        /// </summary>
        public static string StripHtmlAndDiacritics(string s)
        {
            var  sb    = new StringBuilder(s.Length);
            bool inTag = false;
            foreach (char c in s)
            {
                if (c == '<') { inTag = true;  continue; }
                if (c == '>') { inTag = false; continue; }
                if (inTag) continue;
                if (c >= '\u0591' && c <= '\u05C7') continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Truncate(string s, int max) =>
            s == null ? string.Empty :
            s.Length <= max ? s : s.Substring(0, max) + "…";

        // ── Reporting ─────────────────────────────────────────────────

        public static void Banner(TextWriter w, string text)
        {
            string line = new string('═', Math.Min(text.Length + 4, 72));
            W(w, line);
            W(w, $"  {text}");
            W(w, line);
        }

        public static void Section(TextWriter w, string text)
        {
            W(w, string.Empty);
            W(w, $"── {text} " + new string('─', Math.Max(0, 60 - text.Length)));
        }

        public static void W(TextWriter w, string msg)
        {
            Console.WriteLine(msg);
            w?.WriteLine(msg);
            w?.Flush();
        }

        public static string FormatElapsed(TimeSpan t) =>
            t.TotalHours >= 1
                ? $"{(int)t.TotalHours}h {t.Minutes:D2}m {t.Seconds:D2}s"
                : t.TotalMinutes >= 1
                    ? $"{(int)t.TotalMinutes}m {t.Seconds:D2}s"
                    : $"{t.TotalSeconds:F2}s";

        public static string FormatRate(long count, TimeSpan elapsed) =>
            elapsed.TotalSeconds > 0
                ? $"{count / elapsed.TotalSeconds:N0}/s"
                : "—";
    }
}
