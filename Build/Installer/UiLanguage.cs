using KleiKodesh.Helpers;
using System.Globalization;
using System.Threading;
using System.Windows;

namespace KleiKodeshVstoInstallerWpf
{
    /// <summary>
    /// Installer UI language. Texts come from Properties/Strings.resx (Hebrew, default)
    /// and Strings.en.resx (English); this only picks the UI culture and derives the
    /// layout direction from it. The choice is persisted so the next run (and the
    /// elevated relaunch for deep clean) opens in the same language.
    /// </summary>
    public static class UiLanguage
    {
        const string Section = "Installer";
        const string Key     = "UiLanguage";

        public const string Hebrew  = "he";
        public const string English = "en";

        public static bool IsRightToLeft => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;

        public static FlowDirection FlowDirection =>
            IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        public static MessageBoxOptions MessageBoxOptions =>
            IsRightToLeft ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None;

        public static string Current =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == English ? English : Hebrew;

        /// <summary>Applies the saved language. Call before the first window is created.</summary>
        public static void ApplySaved()
        {
            string lang = SettingsManager.Get(Section, Key, Hebrew);
            Apply(lang == English ? English : Hebrew);
        }

        /// <summary>Sets the UI culture for this process and remembers it.</summary>
        public static void Set(string lang)
        {
            Apply(lang);
            SettingsManager.Save(Section, Key, lang);
        }

        static void Apply(string lang)
        {
            var culture = new CultureInfo(lang == English ? "en-US" : "he-IL");
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentUICulture     = culture;
        }
    }
}
