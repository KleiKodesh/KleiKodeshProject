using System.Collections.ObjectModel;

namespace RegexFindLib.UI
{
    public partial class RegexFindViewModel
    {
        // ── Palette tip sets — static, built once ─────────────────────────────

        /// <summary>.NET regex — find box</summary>
        static readonly ObservableCollection<RegexTip> TipsRegexFind =
            new ObservableCollection<RegexTip>
        {
            new RegexTip(".",        "כל תו בודד",                       "a.b → acb, a1b"),
            new RegexTip("*",        "אפס או יותר מהקודם",               "a* → '', a, aa"),
            new RegexTip("+",        "אחד או יותר מהקודם",               "a+ → a, aa"),
            new RegexTip("?",        "אפס או אחד מהקודם",                "a? → '', a"),
            new RegexTip("\\d",      "ספרה (0-9)",                       "\\d{4} → 2024"),
            new RegexTip("\\w",      "תו מילה (אות/ספרה/_)",             "\\w+ → שלום"),
            new RegexTip("\\s",      "רווח לבן",                         "\\s+ → רווח, טאב"),
            new RegexTip("[...]",    "קבוצת תווים",                      "[אבג] → א, ב, או ג"),
            new RegexTip("[^...]",   "שלילת קבוצה",                      "[^0-9] → לא ספרה"),
            new RegexTip("[a-z]",    "טווח תווים",                       "[א-ת] → כל אות עברית"),
            new RegexTip("^",        "תחילת שורה",                       "^שלום → שלום בתחילה"),
            new RegexTip("$",        "סוף שורה",                         "שלום$ → שלום בסוף"),
            new RegexTip("|",        "או",                               "כלב|חתול"),
            new RegexTip("(...)",    "קבוצת לכידה",                      "(\\w+)\\s(\\w+)"),
            new RegexTip("(?:...)",  "קבוצה ללא לכידה",                  "(?:אב)+"),
            new RegexTip("\\b",      "גבול מילה",                        "\\bשלום\\b"),
            new RegexTip("(?=...)",  "ציפייה חיובית",                    "א(?=ב) → א לפני ב"),
            new RegexTip("(?!...)",  "ציפייה שלילית",                    "א(?!ב) → א לא לפני ב"),
            new RegexTip("{n}",      "בדיוק n חזרות",                    "\\d{4} → 4 ספרות"),
            new RegexTip("{n,m}",    "בין n ל-m חזרות",                  "\\d{2,4}"),
            new RegexTip(".*?",      "התאמה עצלה",                       "<.*?> → תג בודד"),
        };

        /// <summary>.NET regex — replace box (adds back-references)</summary>
        static readonly ObservableCollection<RegexTip> TipsRegexReplace =
            new ObservableCollection<RegexTip>
        {
            new RegexTip("$0",       "כל הטקסט שנמצא",                  "$0 → הטקסט המלא"),
            new RegexTip("$1",       "קבוצה ראשונה",                     "(\\w+) → $1"),
            new RegexTip("$2",       "קבוצה שנייה",                      "(\\w+) (\\w+) → $2 $1"),
            new RegexTip("${name}",  "קבוצה בשם",                        "(?<n>\\w+) → ${n}"),
            new RegexTip("$`",       "טקסט לפני ההתאמה",                 ""),
            new RegexTip("$'",       "טקסט אחרי ההתאמה",                 ""),
        };

        /// <summary>Word plain search — find box (^-codes, no wildcards)</summary>
        static readonly ObservableCollection<RegexTip> TipsWordPlainFind =
            new ObservableCollection<RegexTip>
        {
            new RegexTip("^p",  "סימן פסקה",                             "^p^p → שתי פסקאות"),
            new RegexTip("^t",  "טאב",                                   "^t → תו טאב"),
            new RegexTip("^l",  "מעבר שורה (Shift+Enter)",               "^l → שורה חדשה"),
            new RegexTip("^m",  "מעבר עמוד ידני",                        ""),
            new RegexTip("^n",  "מעבר עמודה",                            ""),
            new RegexTip("^s",  "רווח שאינו נשבר",                       ""),
            new RegexTip("^~",  "מקף שאינו נשבר",                        ""),
            new RegexTip("^-",  "מקף אופציונלי",                         ""),
            new RegexTip("^^",  "תו ^",                                  ""),
            new RegexTip("^?",  "כל תו בודד",                            "^? → כל תו"),
            new RegexTip("^#",  "כל ספרה",                               "^# → 0-9"),
            new RegexTip("^$",  "כל אות",                                "^$ → a-z, א-ת"),
            new RegexTip("^w",  "כל רווח לבן",                           "^w → רווח, טאב"),
        };

        /// <summary>Word plain search — replace box (adds ^& and ^c)</summary>
        static readonly ObservableCollection<RegexTip> TipsWordPlainReplace =
            new ObservableCollection<RegexTip>
        {
            new RegexTip("^&",  "הטקסט שנמצא",                           "^& → כל ההתאמה"),
            new RegexTip("^c",  "תוכן הלוח",                             ""),
            new RegexTip("^p",  "סימן פסקה",                             ""),
            new RegexTip("^t",  "טאב",                                   ""),
            new RegexTip("^l",  "מעבר שורה",                             ""),
            new RegexTip("^m",  "מעבר עמוד ידני",                        ""),
            new RegexTip("^s",  "רווח שאינו נשבר",                       ""),
            new RegexTip("^~",  "מקף שאינו נשבר",                        ""),
            new RegexTip("^^",  "תו ^",                                  ""),
        };

        /// <summary>Word wildcard mode — find box</summary>
        static readonly ObservableCollection<RegexTip> TipsWordWildcardFind =
            new ObservableCollection<RegexTip>
        {
            new RegexTip("?",       "כל תו בודד",                        "ד?ג → דבג, דרג"),
            new RegexTip("*",       "אפס תווים או יותר",                 "ד*ג → דג, דבג, דברג"),
            new RegexTip("@",       "אחד או יותר מהקודם",                "דב@ → דב, דבב"),
            new RegexTip("[...]",   "קבוצת תווים",                       "[דהו] → ד, ה, או ו"),
            new RegexTip("[!...]",  "שלילת קבוצה",                       "[!דהו] → לא ד/ה/ו"),
            new RegexTip("[a-z]",   "טווח תווים",                        "[א-ת] → כל אות עברית"),
            new RegexTip("{n}",     "בדיוק n חזרות",                     "ד{3} → דדד"),
            new RegexTip("{n,m}",   "בין n ל-m חזרות",                   "ד{2,4} → דד עד דדדד"),
            new RegexTip("<",       "תחילת מילה",                        "<שלום → שלום, שלומי"),
            new RegexTip(">",       "סוף מילה",                          "שלום> → שלום, ירושלום"),
            new RegexTip("(...)",   "קבוצת לכידה",                       "(\\w+) → לשימוש ב-\\1"),
            new RegexTip("^p",      "סימן פסקה",                         ""),
            new RegexTip("^t",      "טאב",                               ""),
        };

        /// <summary>Word wildcard mode — replace box (adds \1, \2 back-references)</summary>
        static readonly ObservableCollection<RegexTip> TipsWordWildcardReplace =
            new ObservableCollection<RegexTip>
        {
            new RegexTip("\\1",  "קבוצה ראשונה",                         "(שלום) → \\1"),
            new RegexTip("\\2",  "קבוצה שנייה",                          "(\\w+) (\\w+) → \\2 \\1"),
            new RegexTip("^&",   "הטקסט שנמצא",                          ""),
            new RegexTip("^c",   "תוכן הלוח",                            ""),
            new RegexTip("^p",   "סימן פסקה",                            ""),
            new RegexTip("^t",   "טאב",                                  ""),
        };

        // ── PaletteTips — computed, drives the palette panel ─────────────────

        /// <summary>
        /// Returns the correct tip set based on the active engine, wildcard mode, and focused box.
        /// Notified whenever UseWordSearch, UseRegex, or FindFocused changes.
        /// </summary>
        public ObservableCollection<RegexTip> PaletteTips
        {
            get
            {
                if (!UseWordSearch)
                    // Custom .NET regex engine
                    return FindFocused ? TipsRegexFind : BuildCombined(TipsRegexFind, TipsRegexReplace);

                // Word native engine
                if (!UseRegex)
                    return FindFocused ? TipsWordPlainFind : BuildCombined(TipsWordPlainFind, TipsWordPlainReplace);
                else
                    return FindFocused ? TipsWordWildcardFind : BuildCombined(TipsWordWildcardFind, TipsWordWildcardReplace);
            }
        }

        /// <summary>
        /// Combines a base set with replace-specific additions into one list.
        /// The replace items are appended after a visual separator (empty tip).
        /// </summary>
        static ObservableCollection<RegexTip> BuildCombined(
            ObservableCollection<RegexTip> baseSet,
            ObservableCollection<RegexTip> replaceSet)
        {
            var combined = new ObservableCollection<RegexTip>();
            foreach (var t in baseSet)    combined.Add(t);
            combined.Add(new RegexTip("─────", "החלפה בלבד", ""));
            foreach (var t in replaceSet) combined.Add(t);
            return combined;
        }
    }
}
