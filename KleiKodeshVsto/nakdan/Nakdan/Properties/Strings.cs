using System.Globalization;
using System.Resources;
using System.Windows;

namespace Nakdan.Properties
{
    /// <summary>
    /// Pane UI texts. Strings.resx = Hebrew (default), Strings.en.resx = English.
    /// Culture = Thread.CurrentUICulture, set by the host before the pane is built.
    /// English is embedded in Nakdan.dll under "Nakdan.Properties.Strings.en.resources"
    /// (see Nakdan.csproj LogicalName), so no en\ satellite dll is needed.
    /// </summary>
    public static class Strings
    {
        const string BaseName = "Nakdan.Properties.Strings";
        static readonly ResourceManager Manager = new EmbeddedCultureResourceManager();

        static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

        public static bool IsRightToLeft => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;

        /// <summary>Pane chrome direction: RTL for Hebrew, LTR for English.</summary>
        public static FlowDirection ChromeFlowDirection =>
            IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        public static MessageBoxOptions MessageBoxOptions =>
            IsRightToLeft ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None;

        public static string Section_Actions => Get("Section_Actions");
        public static string Action_Vowelize => Get("Action_Vowelize");
        public static string Tip_Vowelize => Get("Tip_Vowelize");
        public static string Action_Clean => Get("Action_Clean");
        public static string Tip_Clean => Get("Tip_Clean");
        public static string Tip_CleanChoose => Get("Tip_CleanChoose");
        public static string Clean_All => Get("Clean_All");
        public static string Clean_KeepEndPunct => Get("Clean_KeepEndPunct");
        public static string Section_Genre => Get("Section_Genre");
        public static string Section_IgnoreStyles => Get("Section_IgnoreStyles");
        public static string ChooseStyles => Get("ChooseStyles");
        public static string Section_Suggestions => Get("Section_Suggestions");
        public static string NextWord => Get("NextWord");
        public static string Tip_Show => Get("Tip_Show");
        public static string Show => Get("Show");
        public static string PrevWord => Get("PrevWord");
        public static string Tip_ApplyOption => Get("Tip_ApplyOption");
        public static string Section_Status => Get("Section_Status");
        public static string Busy => Get("Busy");
        public static string Close => Get("Close");
        public static string Tip_Cancel => Get("Tip_Cancel");
        public static string GenreDesc_Modern => Get("GenreDesc_Modern");
        public static string GenreDesc_Poetry => Get("GenreDesc_Poetry");
        public static string GenreDesc_Bible => Get("GenreDesc_Bible");
        public static string GenreDesc_Rabbinic => Get("GenreDesc_Rabbinic");
        public static string Genre_Rabbinic => Get("Genre_Rabbinic");
        public static string Genre_Bible => Get("Genre_Bible");
        public static string Genre_Modern => Get("Genre_Modern");
        public static string Genre_Poetry => Get("Genre_Poetry");
        public static string Clean_Nikkud => Get("Clean_Nikkud");
        public static string Clean_Cantillations => Get("Clean_Cantillations");
        public static string Clean_Periods => Get("Clean_Periods");
        public static string Clean_Commas => Get("Clean_Commas");
        public static string Clean_Dashes => Get("Clean_Dashes");
        public static string Clean_QuestionExclamation => Get("Clean_QuestionExclamation");
        public static string Clean_Colons => Get("Clean_Colons");
        public static string Status_Done => Get("Status_Done");
        public static string Status_Cancelled => Get("Status_Cancelled");
        public static string Status_Cleaned => Get("Status_Cleaned");
        public static string Hint_ClickOption => Get("Hint_ClickOption");
        public static string Hint_SelectWord => Get("Hint_SelectWord");
        public static string Sugg_Loading => Get("Sugg_Loading");
        public static string Sugg_None => Get("Sugg_None");
        public static string Sugg_LoadFailed => Get("Sugg_LoadFailed");
        public static string Sugg_WordChanged => Get("Sugg_WordChanged");
        public static string Undo_Suggestion => Get("Undo_Suggestion");
        public static string Sugg_ReplaceFailed => Get("Sugg_ReplaceFailed");
        public static string Err_NoInternet => Get("Err_NoInternet");
        public static string Prog_Analyze => Get("Prog_Analyze");
        public static string Prog_Styles => Get("Prog_Styles");
        public static string Prog_Runs => Get("Prog_Runs");
        public static string Prog_Tokens => Get("Prog_Tokens");
        public static string Prog_Chunks => Get("Prog_Chunks");
        public static string Prog_Send => Get("Prog_Send");
        public static string Prog_Apply => Get("Prog_Apply");
        public static string Prog_Write => Get("Prog_Write");
        public static string Prog_Done => Get("Prog_Done");
        public static string Err_NoSelectionVowelize => Get("Err_NoSelectionVowelize");
        public static string Err_NoSelectionClean => Get("Err_NoSelectionClean");
        public static string Err_NothingToClean => Get("Err_NothingToClean");
        public static string Msg_Title => Get("Msg_Title");
        public static string Err_VowelizePrefix => Get("Err_VowelizePrefix");
        public static string Err_Title => Get("Err_Title");

        /// <summary>Resolves culture texts from "&lt;base&gt;.&lt;lang&gt;.resources" inside this dll.</summary>
        sealed class EmbeddedCultureResourceManager : ResourceManager
        {
            public EmbeddedCultureResourceManager() : base(Strings.BaseName, typeof(Strings).Assembly) { }

            protected override ResourceSet InternalGetResourceSet(CultureInfo culture, bool createIfNotExists, bool tryParents)
            {
                if (!culture.Equals(CultureInfo.InvariantCulture))
                {
                    var stream = MainAssembly.GetManifestResourceStream(
                        BaseName + "." + culture.TwoLetterISOLanguageName + ".resources");
                    if (stream != null) return new ResourceSet(stream);
                }
                return base.InternalGetResourceSet(culture, createIfNotExists, tryParents);
            }
        }
    }
}
