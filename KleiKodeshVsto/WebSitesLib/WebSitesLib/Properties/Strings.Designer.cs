// Hand-maintained (not generated). Strings.resx = Hebrew (default),
// Strings.en.resx = English, embedded in this dll (no satellite) and served
// by EmbeddedCultureResourceManager per Thread.CurrentUICulture.
using System.Globalization;
using System.Resources;
using System.Windows;

namespace WebSitesLib.Properties
{
    public static class Strings
    {
        static ResourceManager ResourceManager => EmbeddedCultureResourceManager.Instance;

        static string Get(string key) =>
            ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

        /// <summary>True when the host set an English UI culture.</summary>
        public static bool IsEnglish => Get(nameof(IsEnglish)) == "true";

        /// <summary>Pane chrome direction: LTR for English, RTL for Hebrew.</summary>
        public static FlowDirection PaneFlowDirection =>
            IsEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;

        public static string ShowAllTabs => Get(nameof(ShowAllTabs));
        public static string NewTab => Get(nameof(NewTab));
        public static string CloseTab => Get(nameof(CloseTab));
        public static string PopOut => Get(nameof(PopOut));
        public static string Refresh => Get(nameof(Refresh));
        public static string Back => Get(nameof(Back));
        public static string Forward => Get(nameof(Forward));
        public static string NewTabTitle => Get(nameof(NewTabTitle));
    }

    sealed class EmbeddedCultureResourceManager : ResourceManager
    {
        const string StringsBaseName = "WebSitesLib.Properties.Strings";
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
