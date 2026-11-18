using System.Collections.Generic;
using MessagePack;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// What the publisher allows for one book, from Otzaria's <c>book_protection(bookId, level)</c>
    /// table (schema 7+). The same rules as Otzaria's BookProtection model
    /// (lib/book_protection/models/book_protection.dart), so both apps enforce the same agreements:
    ///   level 0 / no row - no limits;
    ///   level 1          - no editable export (Word / text), at most 40 lines per copy;
    ///   level 2          - also no "Save as PDF", at most 40 lines per print job;
    ///   above 2          - unknown, treated as 2 (the strictest known).
    /// Output that combines several books takes the strictest of them (<see cref="Strictest"/>).
    ///
    /// The rules are computed HERE and sent as-is; the frontend only enforces them.
    /// </summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class BookProtection
    {
        public const int StrictestKnownLevel = 2;
        public const int CopyLineLimit = 40;
        public const int PrintLineLimit = 40;

        public static readonly BookProtection None = ForLevel(0);

        /// <summary>The effective level: 0..<see cref="StrictestKnownLevel"/>.</summary>
        public int Level { get; set; }
        /// <summary>Export to Word / text (formats that are easy to edit).</summary>
        public bool AllowsEditableExport { get; set; }
        /// <summary>"Save as PDF" from the print screen.</summary>
        public bool AllowsPdfExport { get; set; }
        /// <summary>Most lines one copy may hold; null = no limit.</summary>
        public int? MaxCopyLines { get; set; }
        /// <summary>Most lines one print job may hold; null = no limit.</summary>
        public int? MaxPrintLines { get; set; }

        /// <summary>The rules for a raw <c>book_protection.level</c> value.</summary>
        public static BookProtection ForLevel(int rawLevel)
        {
            int level = rawLevel <= 0 ? 0 : rawLevel > StrictestKnownLevel ? StrictestKnownLevel : rawLevel;
            return new BookProtection
            {
                Level = level,
                AllowsEditableExport = level < 1,
                AllowsPdfExport = level < 2,
                MaxCopyLines = level >= 1 ? CopyLineLimit : null,
                MaxPrintLines = level >= 2 ? PrintLineLimit : null,
            };
        }

        /// <summary>The strictest of several books' rules (none given = no limits).</summary>
        public static BookProtection Strictest(IEnumerable<BookProtection?> protections)
        {
            BookProtection result = None;
            foreach (var protection in protections)
                if (protection != null && protection.Level > result.Level) result = protection;
            return result;
        }
    }
}
