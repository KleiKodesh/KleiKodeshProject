using KleiKodesh.Helpers;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Threading;
using System.Windows;

namespace KleiKodeshVstoInstallerWpf
{
    /// <summary>
    /// Installer UI language. Texts come from Properties/Strings.resx (Hebrew, default)
    /// and Strings.en.resx (English); this only picks the UI culture and derives the
    /// layout direction from it. The choice is persisted so the next run (and the
    /// elevated relaunch for deep clean) opens in the same language.
    /// English is embedded in the exe (no en\ satellite dll; the installer ships as
    /// one file), so the generated Strings class gets a ResourceManager that reads it.
    /// </summary>
    public static class UiLanguage
    {
        const string Section = "General"; // shared with the Word add-in (KleiKodeshVsto/Helpers/AddinUiLanguage.cs)
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

        /// <summary>Writes the current language back, e.g. after a clean wiped the settings.</summary>
        public static void Persist() => SettingsManager.Save(Section, Key, Current);

        static void Apply(string lang)
        {
            typeof(Properties.Strings)
                .GetField("resourceMan", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, EmbeddedCultureResourceManager.Instance);

            var culture = new CultureInfo(lang == English ? "en-US" : "he-IL");
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentUICulture     = culture;
        }
    }

    /// <summary>Resolves culture texts from "<base>.<lang>.resources" inside the exe.</summary>
    sealed class EmbeddedCultureResourceManager : ResourceManager
    {
        const string StringsBaseName = "KleiKodeshVstoInstallerWpf.Properties.Strings";
        public static readonly EmbeddedCultureResourceManager Instance = new EmbeddedCultureResourceManager();

        EmbeddedCultureResourceManager() : base(StringsBaseName, typeof(Properties.Strings).Assembly) { }

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
