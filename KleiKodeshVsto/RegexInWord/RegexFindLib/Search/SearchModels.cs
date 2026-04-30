using Microsoft.Office.Interop.Word;

namespace RegexFindLib.Search
{
    /// <summary>
    /// Controls which part of the document is searched.
    /// Mirrors the distinction between Range-based and Selection-based search in Word.
    /// </summary>
    public enum SearchScope
    {
        /// <summary>Search the entire document (or forward/back from cursor depending on Forward).</summary>
        All,
        /// <summary>Search within the current selection only.</summary>
        Selection
    }

    /// <summary>
    /// A single match returned by the search engine.
    /// </summary>
    public class SearchResult
    {
        public Range  Range         { get; set; }
        public int    Start         => Range.Start;
        public int    End           => Range.End;

        /// <summary>Plain text before the match (up to 75 chars).</summary>
        public string ContextBefore { get; set; }

        /// <summary>The matched text itself.</summary>
        public string MatchText     { get; set; }

        /// <summary>Plain text after the match (up to 75 chars).</summary>
        public string ContextAfter  { get; set; }
    }

    /// <summary>
    /// Formatting criteria shared by find and replace sides.
    /// Mirrors the Font + Style properties on Word's Find / Replacement objects.
    /// Null means "don't filter / don't apply".
    /// </summary>
    public class FindFormatting
    {
        public bool?   Bold        { get; set; }
        public bool?   Italic      { get; set; }
        public bool?   Underline   { get; set; }
        public bool?   Superscript { get; set; }
        public bool?   Subscript   { get; set; }
        /// <summary>Font name. Mirrors Word Find.Font.Name. Empty = don't filter/apply.</summary>
        public string  FontName    { get; set; }
        /// <summary>Font size in points. Mirrors Word Find.Font.Size. Null = don't filter/apply.</summary>
        public float?  FontSize    { get; set; }
        /// <summary>Paragraph style name. Mirrors Word Find.Style. Empty = don't filter/apply.</summary>
        public string  Style       { get; set; }
        /// <summary>Raw Word Font.Color decimal. Null = don't filter/apply.</summary>
        public int?    TextColor   { get; set; }
    }

    /// <summary>
    /// Replacement criteria. Mirrors Word's Replacement object.
    /// Nested inside FindRequest as FindRequest.Replacement.
    /// </summary>
    public class FindReplacement
    {
        public string         Text       { get; set; }
        public FindFormatting Formatting { get; set; } = new FindFormatting();
    }

    /// <summary>
    /// Describes a find (and optionally replace) operation.
    /// Mirrors Word's Find object — Text, MatchWildcards, Forward, Replacement, etc.
    /// </summary>
    public class FindRequest
    {
        /// <summary>The text to search for. Mirrors Word Find.Text.</summary>
        public string         Text           { get; set; }

        /// <summary>
        /// True to treat Text as a wildcard/regex pattern.
        /// In custom engine mode: .NET regex.
        /// In Word engine mode: Word wildcard syntax.
        /// Mirrors Word Find.MatchWildcards.
        /// </summary>
        public bool           MatchWildcards { get; set; } = false;

        /// <summary>True to search forward; false to search backward. Mirrors Word Find.Forward.</summary>
        public bool           Forward        { get; set; } = true;

        /// <summary>
        /// True when the user explicitly chose a direction (Forward or Back).
        /// False for "search all" mode — the full document is searched regardless of cursor position.
        /// Not present in Word's API (Word always searches from cursor); custom extension.
        /// </summary>
        public bool           IsDirectional  { get; set; } = false;

        /// <summary>
        /// Whether to search the whole document or only the current selection.
        /// Mirrors the Range vs Selection distinction in Word's Find API.
        /// </summary>
        public SearchScope    Scope          { get; set; } = SearchScope.All;

        /// <summary>
        /// Maximum number of extra words allowed between search terms (fuzzy matching).
        /// Custom extension — not in Word's Find API.
        /// </summary>
        public short          Slop           { get; set; } = 0;

        /// <summary>Formatting criteria for the find side. Mirrors Word Find.Font / Find.Style.</summary>
        public FindFormatting Formatting     { get; set; } = new FindFormatting();

        /// <summary>Replacement text and formatting. Mirrors Word Find.Replacement.</summary>
        public FindReplacement Replacement   { get; set; } = new FindReplacement();
    }
}
