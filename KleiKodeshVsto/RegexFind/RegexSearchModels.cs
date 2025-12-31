using Microsoft.Office.Interop.Word;

namespace KleiKodesh.RegexSearch
{
    public enum RegexSearchMode
    {
        All,
        Forward,
        Back,
        Selection
    }

    public class SearchResult
    {
        public Range Range { get; set; }
        public int Start => Range.Start;
        public int End => Range.End;
        public string Snippet { get; set;  }
    }

    public class RegexFindBase
    {
        public string Text { get; set; }
        public bool? Bold { get; set; }
        public bool? Italic { get; set; }
        public bool? Underline { get; set; }
        public bool? Superscript { get; set; }
        public bool? Subscript { get; set; }
        public string Style { get; set; }
        public string Font { get; set; }
        public float? FontSize { get; set; }
        public int? TextColor { get; set; }
    }

    public class RegexFind : RegexFindBase
    {
        public RegexSearchMode Mode { get; set; } = RegexSearchMode.All;
        public short Slop { get; set; }
        public bool UseWildcards { get; set; } = false;
    }

    public class RegexFindReplace : RegexFindBase
    {

    }

}
