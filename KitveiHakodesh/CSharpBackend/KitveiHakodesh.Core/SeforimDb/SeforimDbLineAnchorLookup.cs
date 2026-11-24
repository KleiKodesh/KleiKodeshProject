using System;
using System.Collections.Generic;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// Finding a line inside ONE book when its TOC has no heading for the place: by a page
    /// marker in the text (<c>&lt;i data-overlay="Vilna Pages" data-value="19a"&gt;</c>), or
    /// by the end of the line's heRef (", <numeral>" - one line per mitzvah in the Rambam's
    /// mitzvah lists). Both read only the given book's rows (idx_line_book_index), the marker
    /// scan stops at the first match. Nothing is cached or built.
    /// </summary>
    public sealed partial class SeforimDbQueries
    {
        /// <summary>
        /// The first lineIndex in [fromIndex, toIndex] of bookId whose text holds a page marker of
        /// <paramref name="overlay"/> with one of <paramref name="values"/>; -1 when none does.
        /// </summary>
        public int FindLineByPageMarker(int bookId, string overlay, IReadOnlyList<string> values, int fromIndex, int toIndex)
        {
            if (string.IsNullOrEmpty(overlay) || values.Count == 0 || toIndex < fromIndex) return -1;
            var needles = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
                needles[i] = $"data-overlay=\"{overlay}\" data-value=\"{values[i]}\"";

            using var conn = Open();
            using var codec = CodecFor(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetLinesInIndexRange(SchemaOf(conn));
            cmd.Parameters.AddWithValue("@bookId", bookId);
            cmd.Parameters.AddWithValue("@from", fromIndex);
            cmd.Parameters.AddWithValue("@to", toIndex);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string content = codec.DecodeColumnOrEmpty(r, 2);
                if (content.IndexOf("data-overlay=", StringComparison.Ordinal) < 0) continue;
                foreach (var needle in needles)
                    if (content.Contains(needle, StringComparison.Ordinal))
                        return r.IsDBNull(1) ? 0 : r.GetInt32(1);
            }
            return -1;
        }

        /// <summary>The first lineIndex of bookId whose heRef ends with <paramref name="suffix"/>; -1 when none.</summary>
        public int FindLineByHeRefSuffix(int bookId, string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return -1;
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.FindLineByHeRefSuffix;
            cmd.Parameters.AddWithValue("@bookId", bookId);
            cmd.Parameters.AddWithValue("@suffix", suffix);
            // substr is exact; LIKE would fold ASCII case and treat % / _ in the suffix as wildcards.
            var found = cmd.ExecuteScalar();
            return found is null or DBNull ? -1 : Convert.ToInt32(found);
        }
    }
}
