using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Nakdan.Core
{

    // ═══════════════════════════════════════════════════════════
    //  DICTA GENRE
    // ═══════════════════════════════════════════════════════════
    public enum DictaGenre
    {
        Bible,
        Rabbinic,
        Modern,
        Poetry,
    }

    // ═══════════════════════════════════════════════════════════
    //  TEXT CLEAN OPTIONS — what the "clean text" button removes
    // ═══════════════════════════════════════════════════════════
    [Flags]
    public enum TextCleanOptions
    {
        None                   = 0,
        Nikkud                 = 1,
        Cantillations          = 2,
        Periods                = 4,
        Dashes                 = 8,
        QuestionAndExclamation = 16,
        Colons                 = 32,
        Commas                 = 64,    // , and ;
        // Not a mark kind: a period, colon, sof pasuq, ? or ! ending a paragraph stays.
        KeepPunctuationAtParagraphEnd = 128,
    }

    // ═══════════════════════════════════════════════════════════
    //  OPTIONS passed in from the API wrapper
    // ═══════════════════════════════════════════════════════════
    public class NakdanOptions
    {
        /// <summary>
        /// Style names to skip (Hebrew or English, case-insensitive).
        /// e.g. new[] { "כותרת 1", "Heading 2", "הדגשה" }
        /// </summary>
        public ICollection<string> IgnoredStyles { get; set; } = new List<string>();

        public DictaGenre Genre { get; set; } = DictaGenre.Modern;
    }
}
