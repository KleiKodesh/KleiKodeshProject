using KitveiHakodesh.Core.Settings;

namespace KitveiHakodeshLib
{
    /// <summary>
    /// Every user-facing text the C# host shows (message boxes, file dialogs, window
    /// chrome, errors sent to the page), in Hebrew and English. Follows the app's
    /// Appearance/UiLanguage setting, read on each use, so a switch applies right away.
    /// Translating the host means editing this file only.
    /// </summary>
    internal static class HostText
    {
        internal static bool English => AppSettingsRegistry.GetUiLanguage() == "en";

        private static string T(string he, string en) => English ? en : he;

        // ── Window chrome (ChromeTabsMirror) ──
        public static string AppTitle => T("כתבי הקודש", "Kitvei Hakodesh");
        public static string OpenTabsHeader => T("לשוניות פתוחות", "Open tabs");
        public static string Minimize => T("מזער", "Minimize");
        public static string Maximize => T("הגדל", "Maximize");
        public static string Restore => T("שחזר", "Restore");
        public static string Close => T("סגור", "Close");
        public static string CloseTab => T("סגור לשונית זו", "Close tab");
        public static string CloseOtherTabs => T("סגור את כל הלשוניות האחרות", "Close other tabs");
        public static string CloseTabsToTheLeft => T("סגור את כל הלשוניות שמשמאל", "Close tabs to the left");
        public static string CloseTabsToTheRight => T("סגור את כל הלשוניות שמימין", "Close tabs to the right");

        // ── Startup errors (AppViewer) ──
        public static string ErrorBoxTitle => T("כזית", "Kezayit");
        public static string InitFailed => T("שגיאה באתחול האפליקציה:", "Error starting the app:");
        public static string EnvironmentDetails => T("── פרטי סביבה ──", "── Environment details ──");
        public static string OtherWordDifferentElevation => T(
            "לא ניתן לפתוח את החלונית בוורד זה, כי וורד אחר כבר פתוח עם הרשאות שונות (אחד מהם רץ כמנהל).\n" +
            "סגרו את כל חלונות וורד ופתחו את הקובץ שוב באופן רגיל (לא כמנהל).",
            "The pane can't open in this Word because another Word is already open with different " +
            "permissions (one of them runs as administrator).\n" +
            "Close all Word windows and open the file again normally (not as administrator).");

        // ── File dialogs ──
        public static string AllFiles => T("כל הקבצים", "All files");
        public static string PickDatabaseTitle => T("בחר קובץ מסד נתונים", "Choose a database file");
        public static string DatabaseFiles => T("מסד נתונים", "Database");
        public static string PickLocalBooksFolder => T("בחר תיקיית ספרים מקומית", "Choose a local books folder");
        public static string OpenFileTitle => T("פתח קובץ", "Open file");
        public static string Documents => T("מסמכים", "Documents");
        public static string SaveBookTitle => T("שמור ספר", "Save book");

        // ── Local files ──
        public static string FileNotFound => T("הקובץ לא נמצא", "File not found");
        public static string FileTypeNotSupported => T("סוג קובץ זה אינו נתמך", "This file type is not supported");
        public static string CannotConvertFile => T("לא ניתן להמיר את הקובץ", "The file could not be converted");
        public static string MakeSureWordInstalled => T("ודא ש-Microsoft Word מותקן.", "Make sure Microsoft Word is installed.");

        // ── HebrewBooks ──
        public static string NoLocalFolderConfigured => T("לא הוגדרה תיקיית שמירה", "No save folder is set");

        // ── Word export / paste ──
        public static string BusyPaste => T("פעולת הדבקה אחרת מתבצעת כעת.", "Another paste is in progress.");
        public static string BusyExport => T("פעולת ייצוא אחרת מתבצעת כעת.", "Another export is in progress.");
        public static string WordBusy => T("וורד עסוק כעת ואינו מגיב. סגור תיבת דו-שיח פתוחה ונסה שוב.",
            "Word is busy and not responding. Close any open dialog and try again.");
        public static string WordConnectionLost => T("החיבור לוורד אבד. נסה שוב.", "The connection to Word was lost. Try again.");
    }
}
