using Microsoft.Win32;
using System;
using System.IO;

namespace KleiKodesh.Helpers
{
    /// <summary>
    /// The optional VBA macros template: one macro per add-in command, so users can
    /// put commands on the Quick Access Toolbar or a shortcut. Two flavours are
    /// shipped in Resources (built by Ribbon\build_macros_template.py): Hebrew macro
    /// names and English macro names. The user picks one (or none) in the installer's
    /// settings page or in the add-in's settings pane; it is never installed silently.
    ///
    /// Installing copies the chosen .dotm into Word's STARTUP folder, which Word loads
    /// as a global template on its next start - so callers tell the user to reopen Word.
    ///
    /// Shared source: linked into the WPF installer (Build\Installer) as well.
    /// </summary>
    public static class VbaMacrosTemplate
    {
        public const string Hebrew  = "he";
        public const string English = "en";

        static readonly string[] Languages = { Hebrew, English };

        // The file name doubles as the record of what is installed: no setting to drift.
        public static string FileName(string lang) => $"KleiKodeshMacros.{lang}.dotm";

        /// <summary>Every name we ever installed, including the first build's
        /// KleiKodeshMacros.dotm (before the he/en split) - for removal.</summary>
        public static readonly string[] AllFileNames =
            { FileName(Hebrew), FileName(English), "KleiKodeshMacros.dotm" };

        /// <summary>
        /// Word's STARTUP folder: templates there load as global add-ins on every Word
        /// start, and it is a trusted location, so their macros run without a prompt.
        /// Honours a user-changed folder (Options → Advanced → File Locations).
        /// </summary>
        public static string StartupFolder
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Office\16.0\Word\Options"))
                        if (key?.GetValue("STARTUP-PATH") is string custom && Directory.Exists(custom))
                            return custom;
                }
                catch { }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    @"Microsoft\Word\STARTUP");
            }
        }

        /// <summary>"he", "en", or null when no macros template is installed.</summary>
        public static string Installed
        {
            get
            {
                foreach (string lang in Languages)
                    if (File.Exists(Path.Combine(StartupFolder, FileName(lang))))
                        return lang;
                return null;
            }
        }

        /// <summary>
        /// Installs the template in <paramref name="lang"/> from
        /// <paramref name="resourcesFolder"/>, replacing any other one; null removes it.
        /// Throws on failure so the caller can tell the user.
        /// </summary>
        public static void Install(string lang, string resourcesFolder)
        {
            string source = lang == null ? null : Path.Combine(resourcesFolder, FileName(lang));
            if (source != null && !File.Exists(source))
                throw new FileNotFoundException(source);

            Remove();
            if (source == null) return;

            Directory.CreateDirectory(StartupFolder);
            File.Copy(source, Path.Combine(StartupFolder, FileName(lang)), overwrite: true);
        }

        // ── UI texts, shared by the installer's settings page and the add-in's pane ──

        public static string HeaderText(bool english) =>
            english ? "VBA macros" : "פקודות מאקרו (VBA)";

        public static string NoneText(bool english) =>
            english ? "None" : "ללא";

        public static string HebrewNamesText(bool english) =>
            english ? "Hebrew macro names" : "שמות בעברית";

        public static string EnglishNamesText(bool english) =>
            english ? "English macro names" : "שמות באנגלית";

        public static string HintText(bool english) =>
            english ? "The add-in's commands as macros, for the Quick Access Toolbar and keyboard shortcuts."
                    : "פקודות התוסף כמאקרו - "
                    + "לסרגל הגישה המהירה "
                    + "ולקיצורי מקשים.";

        public static string ReopenWordText(bool english) =>
            english ? "Restart Word for the change to take effect."
                    : "יש להפעיל מחדש את Word "
                    + "כדי שהשינוי יחול.";

        public static string FailedText(bool english) =>
            english ? "Installing the macros failed:" : "ההתקנה נכשלה:";

        /// <summary>Deletes every flavour from Word's STARTUP folder (uninstall / repair).</summary>
        public static void Remove()
        {
            foreach (string name in AllFileNames)
            {
                string path = Path.Combine(StartupFolder, name);
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}
