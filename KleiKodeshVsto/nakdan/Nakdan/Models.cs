using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Nakdan
{
    // ═══════════════════════════════════════════════════════════
    //  TOKEN — one base character + the nikkud that follows it
    // ═══════════════════════════════════════════════════════════
    internal class Token
    {
        public char Base;
        public string VowelsAfter;
        public int RunIndex;
        public int PosInRun;
    }

    // ═══════════════════════════════════════════════════════════
    //  RUN INFO — mirrors one <w:r> element
    // ═══════════════════════════════════════════════════════════
    internal class RunInfo
    {
        public int Index;
        public XElement Element;
        public XElement TextEl;
        public string OrigText;
        public string StyleName;   // paragraph style that owns this run
    }

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
