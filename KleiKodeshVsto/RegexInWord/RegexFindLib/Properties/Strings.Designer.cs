// Hand-maintained (no designer regeneration). Keep in sync with Strings.resx / Strings.en.resx.
namespace RegexFindLib.Properties
{
    using System.Globalization;
    using System.Resources;

    public static class Strings
    {
        public static ResourceManager ResourceManager => EmbeddedCultureResourceManager.Instance;
        public static CultureInfo Culture { get; set; }

        public static string Undo_ReplaceAll => ResourceManager.GetString("Undo_ReplaceAll", Culture);
        public static string Replace => ResourceManager.GetString("Replace", Culture);
        public static string RegexPalette => ResourceManager.GetString("RegexPalette", Culture);
        public static string WordBuiltInSearch => ResourceManager.GetString("WordBuiltInSearch", Culture);
        public static string SearchDirection => ResourceManager.GetString("SearchDirection", Culture);
        public static string Search => ResourceManager.GetString("Search", Culture);
        public static string SearchHistory => ResourceManager.GetString("SearchHistory", Culture);
        public static string SearchPlaceholder => ResourceManager.GetString("SearchPlaceholder", Culture);
        public static string FindTip_Next => ResourceManager.GetString("FindTip_Next", Culture);
        public static string FindTip_Previous => ResourceManager.GetString("FindTip_Previous", Culture);
        public static string ChooseStyle => ResourceManager.GetString("ChooseStyle", Culture);
        public static string ChooseFont => ResourceManager.GetString("ChooseFont", Culture);
        public static string ReplaceOne => ResourceManager.GetString("ReplaceOne", Culture);
        public static string ReplaceAllButton => ResourceManager.GetString("ReplaceAllButton", Culture);
        public static string ReplaceHistory => ResourceManager.GetString("ReplaceHistory", Culture);
        public static string ReplaceWithPlaceholder => ResourceManager.GetString("ReplaceWithPlaceholder", Culture);
        public static string NoResults => ResourceManager.GetString("NoResults", Culture);
        public static string Mode_All => ResourceManager.GetString("Mode_All", Culture);
        public static string Mode_Down => ResourceManager.GetString("Mode_Down", Culture);
        public static string Mode_Up => ResourceManager.GetString("Mode_Up", Culture);
        public static string Mode_Selection => ResourceManager.GetString("Mode_Selection", Culture);
        public static string UseWordWildcards => ResourceManager.GetString("UseWordWildcards", Culture);
        public static string UseRegex => ResourceManager.GetString("UseRegex", Culture);
        public static string Color_Automatic => ResourceManager.GetString("Color_Automatic", Culture);
        public static string Color_None => ResourceManager.GetString("Color_None", Culture);
        public static string Color_Theme => ResourceManager.GetString("Color_Theme", Culture);
        public static string Color_Standard => ResourceManager.GetString("Color_Standard", Culture);
        public static string Color_More => ResourceManager.GetString("Color_More", Culture);
        public static string FontSizePoints => ResourceManager.GetString("FontSizePoints", Culture);
        public static string Bold => ResourceManager.GetString("Bold", Culture);
        public static string Italic => ResourceManager.GetString("Italic", Culture);
        public static string Underline => ResourceManager.GetString("Underline", Culture);
        public static string Superscript => ResourceManager.GetString("Superscript", Culture);
        public static string Subscript => ResourceManager.GetString("Subscript", Culture);
        public static string FontColor => ResourceManager.GetString("FontColor", Culture);
        public static string ClearFormatting => ResourceManager.GetString("ClearFormatting", Culture);
        public static string PickFormatting => ResourceManager.GetString("PickFormatting", Culture);
        public static string Tip_ReplaceOnly => ResourceManager.GetString("Tip_ReplaceOnly", Culture);
        public static string Tip_AnyChar => ResourceManager.GetString("Tip_AnyChar", Culture);
        public static string Tip_ZeroOrMore => ResourceManager.GetString("Tip_ZeroOrMore", Culture);
        public static string Tip_OneOrMore => ResourceManager.GetString("Tip_OneOrMore", Culture);
        public static string Tip_ZeroOrOne => ResourceManager.GetString("Tip_ZeroOrOne", Culture);
        public static string Tip_Digit => ResourceManager.GetString("Tip_Digit", Culture);
        public static string Tip_WordChar => ResourceManager.GetString("Tip_WordChar", Culture);
        public static string Tip_Whitespace => ResourceManager.GetString("Tip_Whitespace", Culture);
        public static string Tip_CharSet => ResourceManager.GetString("Tip_CharSet", Culture);
        public static string Tip_NegatedSet => ResourceManager.GetString("Tip_NegatedSet", Culture);
        public static string Tip_CharRange => ResourceManager.GetString("Tip_CharRange", Culture);
        public static string Tip_LineStart => ResourceManager.GetString("Tip_LineStart", Culture);
        public static string Tip_LineEnd => ResourceManager.GetString("Tip_LineEnd", Culture);
        public static string Tip_Or => ResourceManager.GetString("Tip_Or", Culture);
        public static string Tip_CaptureGroup => ResourceManager.GetString("Tip_CaptureGroup", Culture);
        public static string Tip_NonCaptureGroup => ResourceManager.GetString("Tip_NonCaptureGroup", Culture);
        public static string Tip_WordBoundary => ResourceManager.GetString("Tip_WordBoundary", Culture);
        public static string Tip_PositiveLookahead => ResourceManager.GetString("Tip_PositiveLookahead", Culture);
        public static string Tip_NegativeLookahead => ResourceManager.GetString("Tip_NegativeLookahead", Culture);
        public static string Tip_ExactlyN => ResourceManager.GetString("Tip_ExactlyN", Culture);
        public static string Tip_NtoM => ResourceManager.GetString("Tip_NtoM", Culture);
        public static string Tip_Lazy => ResourceManager.GetString("Tip_Lazy", Culture);
        public static string Tip_WholeMatch => ResourceManager.GetString("Tip_WholeMatch", Culture);
        public static string Tip_Group1 => ResourceManager.GetString("Tip_Group1", Culture);
        public static string Tip_Group2 => ResourceManager.GetString("Tip_Group2", Culture);
        public static string Tip_NamedGroup => ResourceManager.GetString("Tip_NamedGroup", Culture);
        public static string Tip_BeforeMatch => ResourceManager.GetString("Tip_BeforeMatch", Culture);
        public static string Tip_AfterMatch => ResourceManager.GetString("Tip_AfterMatch", Culture);
        public static string Tip_Paragraph => ResourceManager.GetString("Tip_Paragraph", Culture);
        public static string Tip_Tab => ResourceManager.GetString("Tip_Tab", Culture);
        public static string Tip_LineBreakShift => ResourceManager.GetString("Tip_LineBreakShift", Culture);
        public static string Tip_LineBreak => ResourceManager.GetString("Tip_LineBreak", Culture);
        public static string Tip_PageBreak => ResourceManager.GetString("Tip_PageBreak", Culture);
        public static string Tip_ColumnBreak => ResourceManager.GetString("Tip_ColumnBreak", Culture);
        public static string Tip_NbSpace => ResourceManager.GetString("Tip_NbSpace", Culture);
        public static string Tip_NbHyphen => ResourceManager.GetString("Tip_NbHyphen", Culture);
        public static string Tip_OptHyphen => ResourceManager.GetString("Tip_OptHyphen", Culture);
        public static string Tip_Caret => ResourceManager.GetString("Tip_Caret", Culture);
        public static string Tip_AnyDigit => ResourceManager.GetString("Tip_AnyDigit", Culture);
        public static string Tip_AnyLetter => ResourceManager.GetString("Tip_AnyLetter", Culture);
        public static string Tip_AnyWhitespace => ResourceManager.GetString("Tip_AnyWhitespace", Culture);
        public static string Tip_FoundText => ResourceManager.GetString("Tip_FoundText", Culture);
        public static string Tip_Clipboard => ResourceManager.GetString("Tip_Clipboard", Culture);
        public static string Tip_ZeroOrMoreChars => ResourceManager.GetString("Tip_ZeroOrMoreChars", Culture);
        public static string Tip_WordStart => ResourceManager.GetString("Tip_WordStart", Culture);
        public static string Tip_WordEnd => ResourceManager.GetString("Tip_WordEnd", Culture);
        public static string Status_Replaced => ResourceManager.GetString("Status_Replaced", Culture);
        public static string Status_Found => ResourceManager.GetString("Status_Found", Culture);
    }
}
