//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

//namespace NormalizerTests
//{
//    #region Text Normalizer
//    /// <summary>
//    /// Shared normalization logic used by both indexing and highlighting.
//    /// Keeps only Hebrew alphabet characters (including sofit forms) and lowercase Latin characters.
//    /// Strips HTML tags, treats Hebrew maqaf and underscore as whitespace.
//    /// </summary>
//    public static class TextNormalizer
//    {
//        [ThreadStatic]
//        private static StringBuilder _sb;

//        public static string Normalize(string text)
//        {
//            if (string.IsNullOrWhiteSpace(text)) return "";

//            if (_sb == null) _sb = new StringBuilder(text.Length);
//            else _sb.Clear();

//            bool inTag = false;

//            for (int i = 0; i < text.Length; i++)
//            {
//                char c = text[i];

//                // HTML tag handling
//                if (c == '<')
//                {
//                    inTag = true;
//                    continue;
//                }
//                if (inTag)
//                {
//                    if (c == '>')
//                    {
//                        inTag = false;
//                    }
//                    continue;
//                }

//                // Hebrew maqaf (U+05BE) and underscore are treated as whitespace
//                if (c == '\u05BE' || c == '_')
//                {
//                    _sb.Append(' ');
//                }
//                // Hebrew alphabet characters (U+05D0 to U+05EA includes all letters and sofit forms)
//                else if (c >= '\u05D0' && c <= '\u05EA')
//                {
//                    _sb.Append(c);
//                }
//                // Any whitespace character becomes a space
//                else if (char.IsWhiteSpace(c))
//                {
//                    _sb.Append(' ');
//                }
//                // Latin alphabet - keep as lowercase
//                else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
//                {
//                    _sb.Append(char.ToLowerInvariant(c));
//                }
//                // All other characters (digits, punctuation, etc.) are stripped
//            }

//            return _sb.ToString();
//        }
//    }
//    #endregion

//    #region Test Case Structure
//    public class TestCase
//    {
//        public string Name { get; set; }
//        public string Input { get; set; }
//        public string Expected { get; set; }
//        public string Category { get; set; }
//    }
//    #endregion

//    class Program
//    {
//        static void Main(string[] args)
//        {
//            Console.OutputEncoding = Encoding.UTF8;
//            Console.WriteLine("=".PadRight(80, '='));
//            Console.WriteLine("TEXT NORMALIZER COMPREHENSIVE TEST SUITE");
//            Console.WriteLine("=".PadRight(80, '='));
//            Console.WriteLine();

//            var testCases = new List<TestCase>
//            {
//                // Basic Hebrew text
//                new TestCase
//                {
//                    Category = "Basic Hebrew",
//                    Name = "Simple Hebrew word",
//                    Input = "שלום",
//                    Expected = "שלום"
//                },
//                new TestCase
//                {
//                    Category = "Basic Hebrew",
//                    Name = "Hebrew sentence",
//                    Input = "זה משפט בעברית",
//                    Expected = "זה משפט בעברית"
//                },
//                new TestCase
//                {
//                    Category = "Basic Hebrew",
//                    Name = "Multiple words with spaces",
//                    Input = "תורה   נביאים   כתובים",
//                    Expected = "תורה   נביאים   כתובים"
//                },

//                // Sofit forms (final letters)
//                new TestCase
//                {
//                    Category = "Sofit Forms",
//                    Name = "All sofit letters",
//                    Input = "ךםןףץ",
//                    Expected = "ךםןףץ"
//                },
//                new TestCase
//                {
//                    Category = "Sofit Forms",
//                    Name = "Words with sofit - melech",
//                    Input = "מלך",
//                    Expected = "מלך"
//                },
//                new TestCase
//                {
//                    Category = "Sofit Forms",
//                    Name = "Words with sofit - shamayim",
//                    Input = "שמים",
//                    Expected = "שמים"
//                },
//                new TestCase
//                {
//                    Category = "Sofit Forms",
//                    Name = "Words with sofit - kohen",
//                    Input = "כהן",
//                    Expected = "כהן"
//                },
//                new TestCase
//                {
//                    Category = "Sofit Forms",
//                    Name = "Words with sofit - kaf",
//                    Input = "כף",
//                    Expected = "כף"
//                },
//                new TestCase
//                {
//                    Category = "Sofit Forms",
//                    Name = "Words with sofit - aretz",
//                    Input = "ארץ",
//                    Expected = "ארץ"
//                },

//                // Nikud (vowel points)
//                new TestCase
//                {
//                    Category = "Nikud Removal",
//                    Name = "Kamatz",
//                    Input = "דָּבָר",
//                    Expected = "דבר"
//                },
//                new TestCase
//                {
//                    Category = "Nikud Removal",
//                    Name = "Patach",
//                    Input = "מַלְכָּה",
//                    Expected = "מלכה"
//                },
//                new TestCase
//                {
//                    Category = "Nikud Removal",
//                    Name = "Tzere",
//                    Input = "בֵּית",
//                    Expected = "בית"
//                },
//                new TestCase
//                {
//                    Category = "Nikud Removal",
//                    Name = "Segol",
//                    Input = "מֶלֶךְ",
//                    Expected = "מלך"
//                },
//                new TestCase
//                {
//                    Category = "Nikud Removal",
//                    Name = "Chirik",
//                    Input = "שִׁיר",
//                    Expected = "שיר"
//                },
//                new TestCase
//                {
//                    Category = "Nikud Removal",
//                    Name = "Cholam",
//                    Input = "שָׁלוֹם",
//                    Expected = "שלום"
//                },
//                new TestCase
//                {
//                    Category = "Nikud Removal",
//                    Name = "Kubutz",
//                    Input = "קֻדְשָׁה",
//                    Expected = "קדשה"
//                },
//                new TestCase
//                {
//                    Category = "Nikud Removal",
//                    Name = "Shuruk",
//                    Input = "רוּחַ",
//                    Expected = "רוח"
//                },
//                new TestCase
//                {
//                    Category = "Nikud Removal",
//                    Name = "Shva",
//                    Input = "בְּרֵאשִׁית",
//                    Expected = "בראשית"
//                },
//                new TestCase
//                {
//                    Category = "Nikud Removal",
//                    Name = "Dagesh",
//                    Input = "שַׁבָּת",
//                    Expected = "שבת"
//                },
//                new TestCase
//                {
//                    Category = "Nikud Removal",
//                    Name = "Full sentence with nikud",
//                    Input = "בְּרֵאשִׁית בָּרָא אֱלֹקִים",
//                    Expected = "בראשית ברא אלקים"
//                },

//                // Cantillation marks (te'amim)
//                new TestCase
//                {
//                    Category = "Cantillation Removal",
//                    Name = "Etnachta",
//                    Input = "וַיֹּ֣אמֶר",
//                    Expected = "ויאמר"
//                },
//                new TestCase
//                {
//                    Category = "Cantillation Removal",
//                    Name = "Sof pasuk",
//                    Input = "אֱלֹהִֽים׃",
//                    Expected = "אלהים"
//                },
//                new TestCase
//                {
//                    Category = "Cantillation Removal",
//                    Name = "Multiple te'amim",
//                    Input = "בְּרֵאשִׁ֖ית בָּרָ֣א אֱלֹהִ֑ים",
//                    Expected = "בראשית ברא אלהים"
//                },
//                new TestCase
//                {
//                    Category = "Cantillation Removal",
//                    Name = "Pashta, zakef",
//                    Input = "וַיִּקְרָ֥א אֱלֹהִ֛ים",
//                    Expected = "ויקרא אלהים"
//                },
//                new TestCase
//                {
//                    Category = "Cantillation Removal",
//                    Name = "Munach, tipcha",
//                    Input = "לָא֥וֹר יֽוֹם",
//                    Expected = "לאור יום"
//                },

//                // Hebrew maqaf (hyphen)
//                new TestCase
//                {
//                    Category = "Maqaf Handling",
//                    Name = "Single maqaf",
//                    Input = "בן־אדם",
//                    Expected = "בן אדם"
//                },
//                new TestCase
//                {
//                    Category = "Maqaf Handling",
//                    Name = "Multiple maqaf",
//                    Input = "כל־בית־ישראל",
//                    Expected = "כל בית ישראל"
//                },
//                new TestCase
//                {
//                    Category = "Maqaf Handling",
//                    Name = "Maqaf with nikud",
//                    Input = "בֶּן־אָדָם",
//                    Expected = "בן אדם"
//                },

//                // Underscore as whitespace
//                new TestCase
//                {
//                    Category = "Underscore Handling",
//                    Name = "Single underscore",
//                    Input = "שלום_עולם",
//                    Expected = "שלום עולם"
//                },
//                new TestCase
//                {
//                    Category = "Underscore Handling",
//                    Name = "Multiple underscores",
//                    Input = "אחד__שנים___שלושה",
//                    Expected = "אחד  שנים   שלושה"
//                },

//                // Whitespace handling
//                new TestCase
//                {
//                    Category = "Whitespace Normalization",
//                    Name = "Tabs to spaces",
//                    Input = "שלום\tעולם",
//                    Expected = "שלום עולם"
//                },
//                new TestCase
//                {
//                    Category = "Whitespace Normalization",
//                    Name = "Newlines to spaces",
//                    Input = "שלום\nעולם",
//                    Expected = "שלום עולם"
//                },
//                new TestCase
//                {
//                    Category = "Whitespace Normalization",
//                    Name = "Carriage return to space",
//                    Input = "שלום\rעולם",
//                    Expected = "שלום עולם"
//                },
//                new TestCase
//                {
//                    Category = "Whitespace Normalization",
//                    Name = "Mixed whitespace",
//                    Input = "שלום \t\n\r עולם",
//                    Expected = "שלום     עולם"
//                },
//                new TestCase
//                {
//                    Category = "Whitespace Normalization",
//                    Name = "Non-breaking space",
//                    Input = "שלום\u00A0עולם",
//                    Expected = "שלום עולם"
//                },

//                // HTML tag stripping
//                new TestCase
//                {
//                    Category = "HTML Tag Removal",
//                    Name = "Simple tags",
//                    Input = "<b>שלום</b>",
//                    Expected = "שלום"
//                },
//                new TestCase
//                {
//                    Category = "HTML Tag Removal",
//                    Name = "Nested tags",
//                    Input = "<div><span>עולם</span></div>",
//                    Expected = "עולם"
//                },
//                new TestCase
//                {
//                    Category = "HTML Tag Removal",
//                    Name = "Tags between words",
//                    Input = "שלום<br>עולם",
//                    Expected = "שלוםעולם"
//                },
//                new TestCase
//                {
//                    Category = "HTML Tag Removal",
//                    Name = "Tag in middle of word",
//                    Input = "ש<i>ל</i>ום",
//                    Expected = "שלום"
//                },
//                new TestCase
//                {
//                    Category = "HTML Tag Removal",
//                    Name = "Tags with attributes",
//                    Input = "<span class='hebrew'>תורה</span>",
//                    Expected = "תורה"
//                },
//                new TestCase
//                {
//                    Category = "HTML Tag Removal",
//                    Name = "Self-closing tags",
//                    Input = "שלום<br/>עולם",
//                    Expected = "שלוםעולם"
//                },
//                new TestCase
//                {
//                    Category = "HTML Tag Removal",
//                    Name = "Multiple tags around word",
//                    Input = "<b><i>משנה</i></b>",
//                    Expected = "משנה"
//                },

//                // Punctuation in middle of words
//                new TestCase
//                {
//                    Category = "Punctuation Removal",
//                    Name = "Quote in middle - Rashba",
//                    Input = "רשב\"א",
//                    Expected = "רשבא"
//                },
//                new TestCase
//                {
//                    Category = "Punctuation Removal",
//                    Name = "Quote in middle - Rambam",
//                    Input = "רמב\"ם",
//                    Expected = "רמבם"
//                },
//                new TestCase
//                {
//                    Category = "Punctuation Removal",
//                    Name = "Quote in middle - Rashi",
//                    Input = "רש\"י",
//                    Expected = "רשי"
//                },
//                new TestCase
//                {
//                    Category = "Punctuation Removal",
//                    Name = "Apostrophe in word",
//                    Input = "ר'יוסף",
//                    Expected = "ריוסף"
//                },
//                new TestCase
//                {
//                    Category = "Punctuation Removal",
//                    Name = "Comma in abbreviation",
//                    Input = "תנ,ך",
//                    Expected = "תנך"
//                },
//                new TestCase
//                {
//                    Category = "Punctuation Removal",
//                    Name = "Period in abbreviation",
//                    Input = "ה.ב.",
//                    Expected = "הב"
//                },
//                new TestCase
//                {
//                    Category = "Punctuation Removal",
//                    Name = "Multiple punctuation",
//                    Input = "א\"ב'ג,ד",
//                    Expected = "אבגד"
//                },

//                // Digits removal
//                new TestCase
//                {
//                    Category = "Digit Removal",
//                    Name = "Digits in text",
//                    Input = "פרק 123 דף 45",
//                    Expected = "פרק  דף "
//                },
//                new TestCase
//                {
//                    Category = "Digit Removal",
//                    Name = "Digits attached to words",
//                    Input = "משנה1 הלכה2",
//                    Expected = "משנה הלכה"
//                },

//                // Mixed Hebrew and Latin
//                new TestCase
//                {
//                    Category = "Mixed Text",
//                    Name = "Hebrew and English",
//                    Input = "שלום World",
//                    Expected = "שלום world"
//                },
//                new TestCase
//                {
//                    Category = "Mixed Text",
//                    Name = "English lowercase",
//                    Input = "hello עולם",
//                    Expected = "hello עולם"
//                },
//                new TestCase
//                {
//                    Category = "Mixed Text",
//                    Name = "English uppercase",
//                    Input = "HELLO עולם",
//                    Expected = "hello עולם"
//                },
//                new TestCase
//                {
//                    Category = "Mixed Text",
//                    Name = "Mixed case English",
//                    Input = "HeLLo WoRLd",
//                    Expected = "hello world"
//                },

//                // Complex real-world cases
//                new TestCase
//                {
//                    Category = "Complex Cases",
//                    Name = "Talmud reference with nikud and punctuation",
//                    Input = "גְמָרָא ב\"מ דַּף כ\"ה ע\"א",
//                    Expected = "גמרא במ דף כה עא"
//                },
//                new TestCase
//                {
//                    Category = "Complex Cases",
//                    Name = "Citation with HTML and nikud",
//                    Input = "<span>רַמְבַּ\"ם הִלְכוֹת תְּשׁוּבָה</span>",
//                    Expected = "רמבם הלכות תשובה"
//                },
//                new TestCase
//                {
//                    Category = "Complex Cases",
//                    Name = "Mixed formatting",
//                    Input = "רַשִׁ\"י<br/>עַל_הַתּוֹרָה־פֶּרֶק א'",
//                    Expected = "רשי על התורה פרק א"
//                },
//                new TestCase
//                {
//                    Category = "Complex Cases",
//                    Name = "Multiple issues combined",
//                    Input = "<b>בְּרֵאשִׁ֖ית</b> בָּרָ֣א <i>אֱלֹהִ֑ים</i> (ר\"ה)",
//                    Expected = "בראשית ברא אלהים רה"
//                },

//                // Edge cases
//                new TestCase
//                {
//                    Category = "Edge Cases",
//                    Name = "Empty string",
//                    Input = "",
//                    Expected = ""
//                },
//                new TestCase
//                {
//                    Category = "Edge Cases",
//                    Name = "Only whitespace",
//                    Input = "   \t\n  ",
//                    Expected = ""
//                },
//                new TestCase
//                {
//                    Category = "Edge Cases",
//                    Name = "Only punctuation",
//                    Input = ".,;:!?",
//                    Expected = ""
//                },
//                new TestCase
//                {
//                    Category = "Edge Cases",
//                    Name = "Only HTML tags",
//                    Input = "<div><span></span></div>",
//                    Expected = ""
//                },
//                new TestCase
//                {
//                    Category = "Edge Cases",
//                    Name = "Only nikud",
//                    Input = "\u05B0\u05B1\u05B2",
//                    Expected = ""
//                },
//                new TestCase
//                {
//                    Category = "Edge Cases",
//                    Name = "Unclosed HTML tag",
//                    Input = "שלום<b>עולם",
//                    Expected = "שלום"
//                },
//                new TestCase
//                {
//                    Category = "Edge Cases",
//                    Name = "GT without LT",
//                    Input = "שלום>עולם",
//                    Expected = "שלוםעולם"
//                },

//                // Special symbols
//                new TestCase
//                {
//                    Category = "Special Symbols",
//                    Name = "Parentheses",
//                    Input = "שלום (עולם)",
//                    Expected = "שלום עולם"
//                },
//                new TestCase
//                {
//                    Category = "Special Symbols",
//                    Name = "Brackets",
//                    Input = "שלום [עולם]",
//                    Expected = "שלום עולם"
//                },
//                new TestCase
//                {
//                    Category = "Special Symbols",
//                    Name = "Braces",
//                    Input = "שלום {עולם}",
//                    Expected = "שלום עולם"
//                },
//                new TestCase
//                {
//                    Category = "Special Symbols",
//                    Name = "Mathematical operators",
//                    Input = "א+ב-ג*ד/ה",
//                    Expected = "אבגדה"
//                },
//                new TestCase
//                {
//                    Category = "Special Symbols",
//                    Name = "Currency symbols",
//                    Input = "$100 שקלים €50",
//                    Expected = " שקלים "
//                },

//                // Geresh and Gershayim (commonly used in Hebrew)
//                new TestCase
//                {
//                    Category = "Geresh/Gershayim",
//                    Name = "Geresh (single quote for numbers)",
//                    Input = "ה' אלפים",
//                    Expected = "ה אלפים"
//                },
//                new TestCase
//                {
//                    Category = "Geresh/Gershayim",
//                    Name = "Gershayim (double quote for numbers)",
//                    Input = "ט\"ו",
//                    Expected = "טו"
//                },
//            };

//            RunTests(testCases);

//            Console.WriteLine();
//            Console.WriteLine("Press any key to exit...");
//            Console.ReadKey();
//        }

//        static void RunTests(List<TestCase> testCases)
//        {
//            int passed = 0;
//            int failed = 0;
//            string currentCategory = "";

//            foreach (var test in testCases)
//            {
//                if (test.Category != currentCategory)
//                {
//                    currentCategory = test.Category;
//                    Console.WriteLine();
//                    Console.WriteLine($"--- {currentCategory} ---");
//                }

//                string result = TextNormalizer.Normalize(test.Input);
//                bool success = result == test.Expected;

//                if (success)
//                {
//                    passed++;
//                    Console.ForegroundColor = ConsoleColor.Green;
//                    Console.Write("✓ ");
//                }
//                else
//                {
//                    failed++;
//                    Console.ForegroundColor = ConsoleColor.Red;
//                    Console.Write("✗ ");
//                }

//                Console.ResetColor();
//                Console.WriteLine($"{test.Name}");

//                if (!success)
//                {
//                    Console.ForegroundColor = ConsoleColor.Yellow;
//                    Console.WriteLine($"  Input:    '{test.Input}'");
//                    Console.WriteLine($"  Expected: '{test.Expected}'");
//                    Console.WriteLine($"  Got:      '{result}'");
//                    Console.ResetColor();
//                }
//            }

//            Console.WriteLine();
//            Console.WriteLine("=".PadRight(80, '='));
//            Console.WriteLine($"RESULTS: {passed} passed, {failed} failed out of {testCases.Count} tests");

//            double percentage = (double)passed / testCases.Count * 100;
//            Console.ForegroundColor = percentage == 100 ? ConsoleColor.Green :
//                                     percentage >= 80 ? ConsoleColor.Yellow : ConsoleColor.Red;
//            Console.WriteLine($"Success Rate: {percentage:F1}%");
//            Console.ResetColor();
//            Console.WriteLine("=".PadRight(80, '='));
//        }
//    }
//}
