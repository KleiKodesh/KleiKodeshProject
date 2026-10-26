using System.Globalization;
using System.Resources;
using System.Windows;

namespace RegexFindLib.Properties
{
    /// <summary>
    /// Pane language. The host sets Thread.CurrentUICulture (en-US or he-IL) before
    /// constructing the pane. Hebrew (Strings.resx) is the default; English
    /// (Strings.en.resx) is embedded in this assembly under
    /// 'RegexFindLib.Properties.Strings.en.resources' (no satellite dll).
    /// </summary>
    public static class UiLanguage
    {
        public static bool IsEnglish =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";

        /// <summary>Pane chrome direction: LTR for English, RTL for Hebrew.</summary>
        public static FlowDirection FlowDirection =>
            IsEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
    }

    /// <summary>Reads '&lt;base&gt;.&lt;lang&gt;.resources' straight from the main assembly.</summary>
    sealed class EmbeddedCultureResourceManager : ResourceManager
    {
        const string StringsBaseName = "RegexFindLib.Properties.Strings";
        public static readonly EmbeddedCultureResourceManager Instance = new EmbeddedCultureResourceManager();

        EmbeddedCultureResourceManager() : base(StringsBaseName, typeof(Strings).Assembly) { }

        protected override ResourceSet InternalGetResourceSet(CultureInfo culture, bool createIfNotExists, bool tryParents)
        {
            if (!culture.Equals(CultureInfo.InvariantCulture))
            {
                var stream = MainAssembly.GetManifestResourceStream(
                    StringsBaseName + "." + culture.TwoLetterISOLanguageName + ".resources");
                if (stream != null) return new ResourceSet(stream);
            }
            return base.InternalGetResourceSet(culture, createIfNotExists, tryParents);
        }
    }
}
