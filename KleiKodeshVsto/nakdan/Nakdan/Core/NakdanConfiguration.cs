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
