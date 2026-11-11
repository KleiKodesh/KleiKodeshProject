using System.Collections.Generic;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// The category a related book is shelved under in the commentary lists ("Rishonim",
    /// "Acharonim", "Commentaries on X" ...), derived from the book's catalogue folder chain.
    ///
    /// The same rules as the frontend's findCategoryMeta (book-catalog/bookCatalogTree.ts) and
    /// resolveCategory (commentary/commentaryGroupBuilder.ts), which the commentary panel still
    /// uses - the related-books list must shelve a book exactly where the panel does.
    /// Hebrew literals are written as escapes so this file stays ASCII.
    /// </summary>
    internal static class SeforimDbBookCommentaryCategory
    {
        public const string OtherCategory = "\u05D0\u05D7\u05E8";
        private const string AlSuffix = " \u05E2\u05DC";
        private const string Rishonim = "\u05E8\u05D0\u05E9\u05D5\u05E0\u05D9\u05DD";
        private const string Acharonim = "\u05D0\u05D7\u05E8\u05D5\u05E0\u05D9\u05DD";
        private const string MechabreiZmanenu = "\u05DE\u05D7\u05D1\u05E8\u05D9 \u05D6\u05DE\u05E0\u05E0\u05D5";
        private const string Midrash = "\u05DE\u05D3\u05E8\u05E9";
        private const string Talmud = "\u05EA\u05DC\u05DE\u05D5\u05D3";
        private const string Tosefta = "\u05EA\u05D5\u05E1\u05E4\u05EA\u05D0";
        private const string Tanach = "\u05EA\u05E0\"\u05DA";
        private const string Mikra = "\u05DE\u05E7\u05E8\u05D0";
        private const string Mishna = "\u05DE\u05E9\u05E0\u05D4";
        private const string Geonim = "\u05D2\u05D0\u05D5\u05E0\u05D9\u05DD";
        private const string Chavruta = "\u05D7\u05D1\u05E8\u05D5\u05EA\u05D0";
        private const string Mefarshim = "\u05DE\u05E4\u05E8\u05E9\u05D9\u05DD";

        private static readonly string[] CategoryOrder =
            { Tanach, Mishna, Tosefta, Talmud, Midrash, Geonim, Rishonim, Acharonim, OtherCategory };

        private static readonly (string Keyword, string Period)[] PeriodKeywords =
        {
            (Rishonim, Rishonim), (Acharonim, Acharonim), (MechabreiZmanenu, MechabreiZmanenu),
            (Midrash, Midrash), (Talmud, Talmud), (Tosefta, Tosefta), (Tanach, Tanach), (Mikra, Tanach),
        };

        private static readonly string[] CommentaryBucketMarkers =
        {
            "\u05E2\u05DC \u05D4\u05EA\u05E0\u05F4\u05DA",
            "\u05E2\u05DC \u05D4\u05EA\u05E0\"\u05DA",
            "\u05E2\u05DC \u05D4\u05EA\u05DC\u05DE\u05D5\u05D3",
            "\u05E2\u05DC \u05D4\u05DE\u05E9\u05E0\u05D4",
            "\u05E2\u05DC \u05D4\u05DE\u05E9\u05E0\u05D9\u05D5\u05EA",
            "\u05E2\u05DC \u05D4\u05E9\"\u05E1",
            "\u05E2\u05DC \u05D4\u05E9\u05E1",
        };

        /// <summary>The list category of a book in <paramref name="categoryId"/> (resolveCategory).</summary>
        public static string Resolve(int categoryId, IReadOnlyDictionary<int, CategoryRow> categories)
        {
            var (period, root) = FindMeta(categoryId, categories);
            period ??= OtherCategory;
            if (period.Length > 0 && period != OtherCategory) return TruncateAtAl(period);
            return TruncateAtAl(root ?? OtherCategory);
        }

        /// <summary>Category rank for section order; unknown categories rank with "other".</summary>
        public static int Rank(string category)
        {
            int index = System.Array.IndexOf(CategoryOrder, category);
            return index == -1 ? CategoryOrder.Length - 1 : index;
        }

        private static string TruncateAtAl(string label)
        {
            int index = label.IndexOf(AlSuffix, System.StringComparison.Ordinal);
            return index != -1 ? label.Substring(0, index) : label;
        }

        private static string? DetectPeriod(string title)
        {
            if (title == Mishna) return Mishna;
            foreach (var (keyword, period) in PeriodKeywords)
                if (title.Contains(keyword)) return period;
            return null;
        }

        /// <summary>findCategoryMeta, rule for rule - including that an early return reports no
        /// root (the root is only known once the walk reaches it).</summary>
        private static (string? Period, string? Root) FindMeta(int categoryId, IReadOnlyDictionary<int, CategoryRow> categories)
        {
            var visited = new HashSet<int>();
            CategoryRow? rootCat = null;
            string? mefarshimParentTitle = null;
            string? fallbackPeriod = null;

            int? currentId = categoryId;
            while (currentId is int id)
            {
                if (!visited.Add(id)) break;
                if (!categories.TryGetValue(id, out var cat)) break;
                if (cat.ParentId == null)
                {
                    rootCat = cat;
                    break;
                }

                string title = cat.Title;
                if (title == "\u05D1\u05D9\u05D0\u05D5\u05E8 " + Chavruta || title == "\u05D4\u05E2\u05E8\u05D5\u05EA \u05E2\u05DC \u05D1\u05D9\u05D0\u05D5\u05E8 " + Chavruta)
                    return (Chavruta, null);
                foreach (var marker in CommentaryBucketMarkers)
                    if (title.Contains(marker)) return (title, null);
                if (title.Contains("\u05D7\u05E1\u05D9\u05D3\u05D5\u05EA")) return ("\u05D7\u05E1\u05D9\u05D3\u05D5\u05EA", null);
                if (title.Contains("\u05DE\u05D9\u05DC\u05D5\u05E0\u05D9\u05DD")) return ("\u05DE\u05D9\u05DC\u05D5\u05E0\u05D9\u05DD", null);
                if (title == MechabreiZmanenu) return (MechabreiZmanenu, null);
                if (title == Rishonim) return (Rishonim, null);

                if (title == Mefarshim && mefarshimParentTitle == null)
                    mefarshimParentTitle = cat.ParentId is int p && categories.TryGetValue(p, out var parent) ? parent.Title : "";

                fallbackPeriod ??= DetectPeriod(title);
                currentId = cat.ParentId;
            }

            if (mefarshimParentTitle != null)
                return (Mefarshim + " \u05E2\u05DC " + mefarshimParentTitle, rootCat?.Title);
            return (fallbackPeriod ?? rootCat?.Title, rootCat?.Title);
        }
    }
}
