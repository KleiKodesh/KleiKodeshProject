using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace RegexFindLib.UI
{
    /// <summary>
    /// Scrollable list of regex tips. Template defined in PaletteStyles.xaml.
    /// InsertAction is wired by the view to insert the symbol at the cursor.
    /// </summary>
    public class RegexPalettePanel : Control
    {
        // ── Tips data — exposed as a DP so the template can bind to it ────────
        public static readonly DependencyProperty TipsProperty =
            DependencyProperty.Register(nameof(Tips), typeof(ObservableCollection<RegexTip>),
                typeof(RegexPalettePanel));

        public ObservableCollection<RegexTip> Tips
        {
            get => (ObservableCollection<RegexTip>)GetValue(TipsProperty);
            set => SetValue(TipsProperty, value);
        }

        // ── InsertAction — wired by the view ──────────────────────────────────
        public static readonly DependencyProperty InsertActionProperty =
            DependencyProperty.Register(nameof(InsertAction), typeof(Action<string>),
                typeof(RegexPalettePanel));

        public Action<string> InsertAction
        {
            get => (Action<string>)GetValue(InsertActionProperty);
            set => SetValue(InsertActionProperty, value);
        }

        public RegexPalettePanel()
        {
            Tips = new ObservableCollection<RegexTip>
            {
                new RegexTip(".",        "כל תו בודד",                        "a.b → acb, a1b"),
                new RegexTip("*",        "אפס או יותר מהאלמנט הקודם",        "a* → '', a, aa"),
                new RegexTip("+",        "אחד או יותר מהאלמנט הקודם",        "a+ → a, aa"),
                new RegexTip("?",        "אפס או אחד מהאלמנט הקודם",         "a? → '', a"),
                new RegexTip("\\d",      "ספרה",                              "\\d → 0-9"),
                new RegexTip("\\w",      "תו מילה",                           "\\w → a-z, 0-9, _"),
                new RegexTip("\\s",      "רווח לבן",                          "\\s → רווח, טאבים, שורות חדשות"),
                new RegexTip("[abc]",    "כל אחד מהתווים",                    "[אבג] → א' או ב' או ג'"),
                new RegexTip("[0-9]",    "כל ספרה",                           "[0-9] → ספרה כלשהי"),
                new RegexTip("^",        "תחילת מחרוזת או שורה",             "^אבג → המחרוזת אבג בתחילה"),
                new RegexTip("$",        "סיום מחרוזת או שורה",              "אבג$ → המחרוזת אבג בסוף"),
                new RegexTip("|",        "אלטרנטיבה (או)",                    "שור|כבש → שור או כבש"),
                new RegexTip("(...)",    "קבוצת לכידה",                       "(אב)+ → אב, אבאב"),
                new RegexTip("\\b",      "גבול מילה",                         "\\bכבש\\b → כבש אבל לא כבשה"),
                new RegexTip("(?:...)",  "קבוצה לא לוכדת",                    "(?:ab)+ → ללא לכידה"),
                new RegexTip("(?=...)",  "ציפייה חיובית",                     "א(?=ב) → א לפני ב"),
                new RegexTip("(?!...)",  "ציפייה שלילית",                     "א(?!ב) → א לא לפני ב"),
                new RegexTip("(?<=...)", "ציפייה לאחור חיובית",               "(?<=א)ב → ב אחרי א"),
                new RegexTip("(?<!...)", "ציפייה לאחור שלילית",               "(?<!א)ב → ב לא אחרי א"),
                new RegexTip("{n}",      "בדיוק n חזרות",                     "a{3} → aaa"),
                new RegexTip("{n,m}",    "בין n ל-m חזרות",                   "a{2,4} → aa, aaa, aaaa"),
                new RegexTip(".*?",      "התאמה עצלה",                        "(ב.*?) → בב מתוך בבבב"),
                new RegexTip("$1",       "התייחסות לקבוצה",                   "$1 → קבוצה מספר 1"),
                new RegexTip("$0",       "כל הטקסט שנמצא",                    "$0 → כל הטקסט שהתאים"),
                new RegexTip("$1, $2",   "קבוצות לכידה מרובות",               "(\\w+) (\\w+) → $2 $1"),
            };
        }
    }

    public class RegexTip
    {
        public string Symbol  { get; }
        public string Meaning { get; }
        public string Example { get; }

        public RegexTip(string symbol, string meaning, string example)
        {
            Symbol  = symbol;
            Meaning = meaning;
            Example = example;
        }
    }
}
