using System.Collections.Generic;

namespace KleiKodesh.Ribbon
{
    /// <summary>
    /// Every user-facing name of the add-in's commands, in Hebrew and English, keyed
    /// by the ribbon control id. The ribbon labels and tips, the context-menu items
    /// and the task-pane titles all come from here, so translating the add-in means
    /// editing this table. VbaCommands mirrors the labels as VBA command names.
    /// </summary>
    internal static class AddinText
    {
        // Follows the General/UiLanguage setting (see Helpers/AddinUiLanguage.cs).
        internal static bool English => Helpers.AddinUiLanguage.IsEnglish;

        internal static string Label(string id) => Pick(Find(id)?.LabelHe, Find(id)?.LabelEn) ?? id;

        internal static string Tip(string id) => Pick(Find(id)?.TipHe, Find(id)?.TipEn);

        private static string Pick(string he, string en) => English ? en ?? he : he;

        private static Entry Find(string id) => id != null && Entries.TryGetValue(id, out var e) ? e : null;

        private sealed class Entry
        {
            public readonly string LabelHe, LabelEn, TipHe, TipEn;

            public Entry(string labelHe, string labelEn, string tipHe, string tipEn)
            {
                LabelHe = labelHe; LabelEn = labelEn; TipHe = tipHe; TipEn = tipEn;
            }
        }

        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>
        {
            { "Klei_Kodesh3", new Entry("כלי קודש", "Klei Kodesh", "ארגז כלים לעורך התורני", "Tools for Torah writing") },
            { "KleiKodesh_Main", new Entry("כלי קודש", "Klei Kodesh", null, null) },
            { "KitveiHakodesh", new Entry("כתבי הקודש", "Kitvei Hakodesh", "תוסף לוורד עבור מאגר הספרים - זית", "Word add-in for the Zayit seforim library") },
            { "Kiwix", new Entry("ספריית קיוויקס", "Kiwix Library", "גישה לספרים ואנציקלופדיות במצב לא מקוון דרך קיוויקס", "Offline access to books and encyclopedias through Kiwix") },
            { "WebSites", new Entry("דרך האתרים", "Websites", "פתיחת אתרים ייעודיים לעורך התורני - בתוך וורד", "Websites for Torah writing - inside Word") },
            { "DocDesign", new Entry("עיצוב תורני", "Torah Design", "כלים לעיצוב ספרי קודש", "Tools for laying out seforim") },
            { "Nakdan", new Entry("נקדן דיקטה", "Dicta Nakdan", "נקדן דיקטה עם אפשרויות משודרגות (דרוש חיבור לאינטרנט)", "Dicta Nakdan with extended options (requires an internet connection)") },
            { "RegexFind", new Entry("חיפוש רגקס", "Regex Find", "חיפוש רגקס בוורד עם אפשרויות עיצוב קלות לשימוש", "Regex search in Word with easy-to-use formatting options") },
            { "DuplicatePane", new Entry("שכפל חלונית צד", "Duplicate Side Pane", "מאפשר פתיחה של חלונית צד נוספת המשכפלת את חלונית הצד הנוכחית", "Opens another side pane that duplicates the current one") },
            { "Settings", new Entry("הגדרות", "Settings", null, null) },
            { "About", new Entry("אודות", "About", null, null) },
            { "SearchText", new Entry("העתק לחיפוש בכתבי הקודש", "Search in Kitvei Hakodesh", "מריץ את הטקסט המסומן כחיפוש בכל ספרי כתבי הקודש. סימנים מיוחדים מוסרים מהטקסט לפני החיפוש.", "Runs the selected text as a search across all Kitvei Hakodesh books. Special characters are removed from the text before searching.") },
            { "SearchBook", new Entry("חיפוש ספר בכתבי הקודש", "Find Book in Kitvei Hakodesh", "מחפש את הטקסט המסומן בקטלוג הספרים של כתבי הקודש - לאיתור ספר לפי שמו.", "Searches the selected text in the Kitvei Hakodesh book catalog - to find a book by its name.") },
            { "CompareText", new Entry("השווה עם כתבי הקודש", "Compare with Kitvei Hakodesh", "משווה את הפסקה המסומנת במסמך אל הטקסט המסומן בחלונית כתבי הקודש: ההבדלים נרשמים במסמך כשינויים עם עקוב, לאישור או דחייה אחד-אחד. יש לסמן תחילה קטע בכתבי הקודש.", "Compares the selected paragraph in the document with the text selected in the Kitvei Hakodesh pane: the differences are written into the document as tracked changes, to accept or reject one by one. First select a passage in Kitvei Hakodesh.") },
            { "OpenLink", new Entry("פתח קישור בכתבי הקודש", "Open Link in Kitvei Hakodesh", "פותח בחלונית כתבי הקודש את הספר שאליו מצביע הקישור שבבחירה.", "Opens in the Kitvei Hakodesh pane the book the link in the selection points to.") },
        };
    }
}
