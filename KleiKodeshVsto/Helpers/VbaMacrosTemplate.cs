using Microsoft.Win32;
using System;
using System.IO;

namespace KleiKodesh.Helpers
{
    /// <summary>
    /// The optional VBA macros template: one macro per add-in command, so users can
    /// put commands on the Quick Access Toolbar or a shortcut. Built into Resources by
    /// Ribbon\build_macros_template.py. Macro names are ASCII only: Hebrew names broke
    /// on machines whose non-Unicode language is not Hebrew ("Sub or Function not
    /// defined" from shortcuts and the Macros dialog, issue #266). The user opts in in
    /// the installer's settings page or in the add-in's settings pane; it is never
    /// installed silently.
    ///
    /// Installing copies the chosen .dotm into Word's STARTUP folder, which Word loads
    /// as a global template on its next start - so callers tell the user to reopen Word.
    ///
    /// Shared source: linked into the WPF installer (Build\Installer) as well.
    /// </summary>
    public static class VbaMacrosTemplate
    {
        // The file name doubles as the record of what is installed: no setting to drift.
        // Kept from the he/en split so existing English-names installs stay valid.
        public const string FileName = "KleiKodeshMacros.en.dotm";

        /// <summary>Every name we ever installed - the first build's KleiKodeshMacros.dotm
        /// and the retired Hebrew-names .he.dotm too - for removal.</summary>
        public static readonly string[] AllFileNames =
            { FileName, "KleiKodeshMacros.he.dotm", "KleiKodeshMacros.dotm" };

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

        /// <summary>True when the current template is installed. A retired Hebrew-names
        /// one counts as not installed, so the user can replace it.</summary>
        public static bool IsInstalled => File.Exists(Path.Combine(StartupFolder, FileName));

        /// <summary>
        /// Installs the template from <paramref name="resourcesFolder"/>, replacing any
        /// older one; <paramref name="install"/> false removes it.
        /// Throws on failure so the caller can tell the user.
        /// </summary>
        public static void Install(bool install, string resourcesFolder)
        {
            string source = Path.Combine(resourcesFolder, FileName);
            if (install && !File.Exists(source))
                throw new FileNotFoundException(source);

            Remove();
            if (!install) return;

            Directory.CreateDirectory(StartupFolder);
            File.Copy(source, Path.Combine(StartupFolder, FileName), overwrite: true);
        }

        // ── UI texts, shared by the installer's settings page and the add-in's pane ──

        public static string HeaderText(bool english) =>
            english ? "VBA macros" : "פקודות מאקרו (VBA)";

        public static string NoneText(bool english) =>
            english ? "None" : "ללא";

        public static string InstallText(bool english) =>
            english ? "Install" : "התקנה";

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
