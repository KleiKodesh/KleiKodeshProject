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
            new RegexTip(".",        Properties.Strings.Tip_AnyChar,                       "a.b → acb, a1b"),
            new RegexTip("*",        Properties.Strings.Tip_ZeroOrMore,               "a* → '', a, aa"),
            new RegexTip("+",        Properties.Strings.Tip_OneOrMore,               "a+ → a, aa"),
            new RegexTip("?",        Properties.Strings.Tip_ZeroOrOne,                "a? → '', a"),
            new RegexTip("\\d",      Properties.Strings.Tip_Digit,                       "\\d{4} → 2024"),
            new RegexTip("\\w",      Properties.Strings.Tip_WordChar,             "\\w+ → שלום"),
            new RegexTip("\\s",      Properties.Strings.Tip_Whitespace,                         "\\s+ → רווח, טאב"),
            new RegexTip("[...]",    Properties.Strings.Tip_CharSet,                      "[אבג] → א, ב, או ג"),
            new RegexTip("[^...]",   Properties.Strings.Tip_NegatedSet,                      "[^0-9] → לא ספרה"),
            new RegexTip("[a-z]",    Properties.Strings.Tip_CharRange,                       "[א-ת] → כל אות עברית"),
            new RegexTip("^",        Properties.Strings.Tip_LineStart,                       "^שלום → שלום בתחילה"),
            new RegexTip("$",        Properties.Strings.Tip_LineEnd,                         "שלום$ → שלום בסוף"),
            new RegexTip("|",        Properties.Strings.Tip_Or,                               "כלב|חתול"),
            new RegexTip("(...)",    Properties.Strings.Tip_CaptureGroup,                      "(\\w+)\\s(\\w+)"),
            new RegexTip("(?:...)",  Properties.Strings.Tip_NonCaptureGroup,                  "(?:אב)+"),
            new RegexTip("\\b",      Properties.Strings.Tip_WordBoundary,                        "\\bשלום\\b"),
            new RegexTip("(?=...)",  Properties.Strings.Tip_PositiveLookahead,                    "א(?=ב) → א לפני ב"),
            new RegexTip("(?!...)",  Properties.Strings.Tip_NegativeLookahead,                    "א(?!ב) → א לא לפני ב"),
            new RegexTip("{n}",      Properties.Strings.Tip_ExactlyN,                    "\\d{4} → 4 ספרות"),
            new RegexTip("{n,m}",    Properties.Strings.Tip_NtoM,                  "\\d{2,4}"),
            new RegexTip(".*?",      Properties.Strings.Tip_Lazy,                       "<.*?> → תג בודד"),
        };

        /// <summary>.NET regex — replace box (adds back-references)</summary>
        static readonly ObservableCollection<RegexTip> TipsRegexReplace =
            new ObservableCollection<RegexTip>
        {
            new RegexTip("$0",       Properties.Strings.Tip_WholeMatch,                  "$0 → הטקסט המלא"),
            new RegexTip("$1",       Properties.Strings.Tip_Group1,                     "(\\w+) → $1"),
            new RegexTip("$2",       Properties.Strings.Tip_Group2,                      "(\\w+) (\\w+) → $2 $1"),
            new RegexTip("${name}",  Properties.Strings.Tip_NamedGroup,                        "(?<n>\\w+) → ${n}"),
            new RegexTip("$`",       Properties.Strings.Tip_BeforeMatch,                 ""),
            new RegexTip("$'",       Properties.Strings.Tip_AfterMatch,                 ""),
        };

        /// <summary>Word plain search — find box (^-codes, no wildcards)</summary>
        static readonly ObservableCollection<RegexTip> TipsWordPlainFind =
            new ObservableCollection<RegexTip>
        {
            new RegexTip("^p",  Properties.Strings.Tip_Paragraph,                             "^p^p → שתי פסקאות"),
            new RegexTip("^t",  Properties.Strings.Tip_Tab,                                   "^t → תו טאב"),
            new RegexTip("^l",  Properties.Strings.Tip_LineBreakShift,               "^l → שורה חדשה"),
            new RegexTip("^m",  Properties.Strings.Tip_PageBreak,                        ""),
            new RegexTip("^n",  Properties.Strings.Tip_ColumnBreak,                            ""),
            new RegexTip("^s",  Properties.Strings.Tip_NbSpace,                       ""),
            new RegexTip("^~",  Properties.Strings.Tip_NbHyphen,                        ""),
            new RegexTip("^-",  Properties.Strings.Tip_OptHyphen,                         ""),
            new RegexTip("^^",  Properties.Strings.Tip_Caret,                                  ""),
            new RegexTip("^?",  Properties.Strings.Tip_AnyChar,                            "^? → כל תו"),
            new RegexTip("^#",  Properties.Strings.Tip_AnyDigit,                               "^# → 0-9"),
            new RegexTip("^$",  Properties.Strings.Tip_AnyLetter,                                "^$ → a-z, א-ת"),
            new RegexTip("^w",  Properties.Strings.Tip_AnyWhitespace,                           "^w → רווח, טאב"),
        };

        /// <summary>Word plain search — replace box (adds ^& and ^c)</summary>
        static readonly ObservableCollection<RegexTip> TipsWordPlainReplace =
            new ObservableCollection<RegexTip>
        {
            new RegexTip("^&",  Properties.Strings.Tip_FoundText,                           "^& → כל ההתאמה"),
            new RegexTip("^c",  Properties.Strings.Tip_Clipboard,                             ""),
            new RegexTip("^p",  Properties.Strings.Tip_Paragraph,                             ""),
            new RegexTip("^t",  Properties.Strings.Tip_Tab,                                   ""),
            new RegexTip("^l",  Properties.Strings.Tip_LineBreak,                             ""),
            new RegexTip("^m",  Properties.Strings.Tip_PageBreak,                        ""),
            new RegexTip("^s",  Properties.Strings.Tip_NbSpace,                       ""),
            new RegexTip("^~",  Properties.Strings.Tip_NbHyphen,                        ""),
            new RegexTip("^^",  Properties.Strings.Tip_Caret,                                  ""),
        };

        /// <summary>Word wildcard mode — find box</summary>
        static readonly ObservableCollection<RegexTip> TipsWordWildcardFind =
            new ObservableCollection<RegexTip>
        {
            new RegexTip("?",       Properties.Strings.Tip_AnyChar,                        "ד?ג → דבג, דרג"),
            new RegexTip("*",       Properties.Strings.Tip_ZeroOrMoreChars,                 "ד*ג → דג, דבג, דברג"),
            new RegexTip("@",       Properties.Strings.Tip_OneOrMore,                "דב@ → דב, דבב"),
            new RegexTip("[...]",   Properties.Strings.Tip_CharSet,                       "[דהו] → ד, ה, או ו"),
            new RegexTip("[!...]",  Properties.Strings.Tip_NegatedSet,                       "[!דהו] → לא ד/ה/ו"),
            new RegexTip("[a-z]",   Properties.Strings.Tip_CharRange,                        "[א-ת] → כל אות עברית"),
            new RegexTip("{n}",     Properties.Strings.Tip_ExactlyN,                     "ד{3} → דדד"),
            new RegexTip("{n,m}",   Properties.Strings.Tip_NtoM,                   "ד{2,4} → דד עד דדדד"),
            new RegexTip("<",       Properties.Strings.Tip_WordStart,                        "<שלום → שלום, שלומי"),
            new RegexTip(">",       Properties.Strings.Tip_WordEnd,                          "שלום> → שלום, ירושלום"),
            new RegexTip("(...)",   Properties.Strings.Tip_CaptureGroup,                       "(\\w+) → לשימוש ב-\\1"),
            new RegexTip("^p",      Properties.Strings.Tip_Paragraph,                         ""),
            new RegexTip("^t",      Properties.Strings.Tip_Tab,                               ""),
        };

        /// <summary>Word wildcard mode — replace box (adds \1, \2 back-references)</summary>
        static readonly ObservableCollection<RegexTip> TipsWordWildcardReplace =
            new ObservableCollection<RegexTip>
        {
            new RegexTip("\\1",  Properties.Strings.Tip_Group1,                         "(שלום) → \\1"),
            new RegexTip("\\2",  Properties.Strings.Tip_Group2,                          "(\\w+) (\\w+) → \\2 \\1"),
            new RegexTip("^&",   Properties.Strings.Tip_FoundText,                          ""),
            new RegexTip("^c",   Properties.Strings.Tip_Clipboard,                            ""),
            new RegexTip("^p",   Properties.Strings.Tip_Paragraph,                            ""),
            new RegexTip("^t",   Properties.Strings.Tip_Tab,                                  ""),
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
            combined.Add(new RegexTip("─────", Properties.Strings.Tip_ReplaceOnly, ""));
            foreach (var t in replaceSet) combined.Add(t);
            return combined;
        }
    }
}
