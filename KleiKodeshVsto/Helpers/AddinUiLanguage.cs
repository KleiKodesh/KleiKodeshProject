using System.Globalization;
using System.Threading;
using System.Windows;

namespace KleiKodesh.Helpers
{
    /// <summary>
    /// The add-in's UI language. Stored in General/UiLanguage ("he" / "en"), the same
    /// value the installer writes (Build/Installer/UiLanguage.cs), so both share it.
    ///
    /// Not applied at add-in startup: <see cref="Apply"/> runs right before a task pane
    /// or the settings view is built, so pane libraries that read
    /// CultureInfo.CurrentUICulture pick it up. Panes already open keep their language;
    /// a full switch needs a Word restart.
    /// </summary>
    public static class AddinUiLanguage
    {
        const string Section = "General";
        const string Key     = "UiLanguage";

        public const string Hebrew  = "he";
        public const string English = "en";

        public static string Current =>
            SettingsManager.Get(Section, Key, Hebrew) == English ? English : Hebrew;

        public static bool IsEnglish => Current == English;

        public static FlowDirection FlowDirection =>
            IsEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;

        /// <summary>Picks the Hebrew or English text for the current language.</summary>
        public static string T(string he, string en) => IsEnglish ? en : he;

        /// <summary>Sets the UI culture of this thread (and new threads) from the saved setting.</summary>
        public static void Apply()
        {
            var culture = new CultureInfo(IsEnglish ? "en-US" : "he-IL");
            if (Thread.CurrentThread.CurrentUICulture.Name == culture.Name) return;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentUICulture     = culture;
        }

        /// <summary>Saves the language and applies it for panes created from now on.</summary>
        public static void Set(string lang)
        {
            SettingsManager.Save(Section, Key, lang == English ? English : Hebrew);
            Apply();
        }
    }
}
