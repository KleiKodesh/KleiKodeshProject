using FtsLib;
using System;
using System.Collections.Generic;

namespace FtsLibTest
{
    internal static class TokenizerTests
    {
        public static void RunAll()
        {
            var t = new Tokenizer();

            // --- existing ---
            TestBasicEnglish(t);
            TestCaseFolding(t);
            TestHebrew(t);
            TestHtmlTags(t);
            TestHtmlEntities(t);
            TestNikudRemoval(t);
            TestMixedInput(t);
            TestNoiseStability(t);

            // --- Tanach battery ---
            TestBereishit(t);
            TestShema(t);
            TestTehillim23(t);
            TestTehillim119(t);
            TestMishleOpening(t);
            TestKoheletOpening(t);
            TestIsaiahOpening(t);
            TestTenCommandments(t);

            // --- Talmud battery ---
            TestMishnaAvot(t);
            TestMishnaBerachot(t);
            TestGemaraBavaMetzia(t);
            TestGemaraShabbat(t);
            TestMishnaAvot2(t);

            // --- Nikud / script edge cases ---
            TestFullNikudPassage(t);
            TestFinalLetterForms(t);
            TestMixedHebrewEnglish(t);
            TestHebrewHtmlWrapped(t);
            TestHebrewEntitiesInline(t);
            TestDuplicateTermDedup(t);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("ALL TOKENIZER TESTS PASSED");
            Console.ResetColor();
        }

        // ================================================================
        // EXISTING TESTS
        // ================================================================

        static void TestBasicEnglish(Tokenizer t)
        {
            var res = t.Extract("hello world hello");
            AssertContains(res, "hello");
            AssertContains(res, "world");
            AssertEqualCount(res, 2);
        }

        static void TestCaseFolding(Tokenizer t)
        {
            var res = t.Extract("HeLLo WoRLd");
            AssertContains(res, "hello");
            AssertContains(res, "world");
        }

        static void TestHebrew(Tokenizer t)
        {
            var res = t.Extract("שלום עולם שלום");
            AssertContains(res, "שלום");
            AssertContains(res, "עולם");
        }

        static void TestHtmlTags(Tokenizer t)
        {
            var res = t.Extract("<div>hello</div> world");
            AssertContains(res, "hello");
            AssertContains(res, "world");
            AssertNotContains(res, "div");

            var res2 = t.Extract("hel<b>lo</b>");
            AssertContains(res2, "hello");
            AssertNotContains(res2, "hel");
            AssertNotContains(res2, "lo");

            var res3 = t.Extract("hel<br>lo");
            AssertContains(res3, "hel");
            AssertContains(res3, "lo");
            AssertNotContains(res3, "hello");
        }

        static void TestHtmlEntities(Tokenizer t)
        {
            var res = t.Extract("a&amp;b &lt;tag&gt;");
            AssertContains(res, "ab");
            AssertContains(res, "tag");
        }

        static void TestNikudRemoval(Tokenizer t)
        {
            var res = t.Extract("שָׁלוֹם");
            AssertContains(res, "שלום");
        }

        static void TestMixedInput(Tokenizer t)
        {
            var res = t.Extract("<p>Hello &amp; שלום</p>");
            AssertContains(res, "hello");
            AssertContains(res, "שלום");
        }

        static void TestNoiseStability(Tokenizer t)
        {
            var res = t.Extract("!!!! <b>&nbsp;&nbsp;</b> ###");
            if (res.Count != 0)
                Fail("Expected empty result for noise input");
        }

        // ================================================================
        // TANACH BATTERY
        // ================================================================

        static void TestBereishit(Tokenizer t)
        {
            var res = t.Extract("בראשית ברא אלהים את השמים ואת הארץ");
            AssertContains(res, "בראשית");
            AssertContains(res, "ברא");
            AssertContains(res, "אלהים");
            AssertContains(res, "השמים");
            AssertContains(res, "הארץ");
            AssertContains(res, "את");
        }

        static void TestShema(Tokenizer t)
        {
            var res = t.Extract("שְׁמַע יִשְׂרָאֵל יְהוָה אֱלֹהֵינוּ יְהוָה אֶחָד");
            AssertContains(res, "שמע");
            AssertContains(res, "ישראל");
            AssertContains(res, "אחד");
            AssertContains(res, "יהוה");
        }

        static void TestTehillim23(Tokenizer t)
        {
            var res = t.Extract("יְהוָה רֹעִי לֹא אֶחְסָר בִּנְאוֹת דֶּשֶׁא יַרְבִּיצֵנִי");
            AssertContains(res, "יהוה");
            AssertContains(res, "רעי");
            AssertContains(res, "דשא");
        }

        static void TestTehillim119(Tokenizer t)
        {
            var res = t.Extract("נֵר לְרַגְלִי דְבָרֶךָ וְאוֹר לִנְתִיבָתִי");
            AssertContains(res, "נר");
            AssertContains(res, "לרגלי");
            AssertContains(res, "דברך");
            AssertContains(res, "ואור");
            AssertContains(res, "לנתיבתי");
        }

        static void TestMishleOpening(Tokenizer t)
        {
            var res = t.Extract("מִשְׁלֵי שְׁלֹמֹה בֶן דָּוִד מֶלֶךְ יִשְׂרָאֵל לָדַעַת חָכְמָה וּמוּסָר");
            AssertContains(res, "משלי");
            AssertContains(res, "שלמה");
            AssertContains(res, "דוד");
            AssertContains(res, "מלך");
            AssertContains(res, "ישראל");
            AssertContains(res, "חכמה");
            AssertContains(res, "ומוסר");
        }

        static void TestKoheletOpening(Tokenizer t)
        {
            var res = t.Extract("דִּבְרֵי קֹהֶלֶת בֶּן דָּוִד מֶלֶךְ בִּירוּשָׁלָיִם הֲבֵל הֲבָלִים אָמַר קֹהֶלֶת");
            AssertContains(res, "דברי");
            AssertContains(res, "קהלת");
            AssertContains(res, "הבל");
            AssertContains(res, "הבלים");
        }

        static void TestIsaiahOpening(Tokenizer t)
        {
            var res = t.Extract("חֲזוֹן יְשַׁעְיָהוּ בֶן אָמוֹץ אֲשֶׁר חָזָה עַל יְהוּדָה וִירוּשָׁלָיִם");
            AssertContains(res, "חזון");
            AssertContains(res, "ישעיהו");
            AssertContains(res, "יהודה");
            AssertContains(res, "וירושלים");
        }

        static void TestTenCommandments(Tokenizer t)
        {
            var res = t.Extract("אָנֹכִי יְהוָה אֱלֹהֶיךָ אֲשֶׁר הוֹצֵאתִיךָ מֵאֶרֶץ מִצְרַיִם מִבֵּית עֲבָדִים לֹא יִהְיֶה לְךָ אֱלֹהִים אֲחֵרִים עַל פָּנָי");
            AssertContains(res, "אנכי");
            AssertContains(res, "יהוה");
            AssertContains(res, "מצרים");
            AssertContains(res, "עבדים");
            AssertContains(res, "אלהים");
            AssertContains(res, "אחרים");
        }

        // ================================================================
        // TALMUD BATTERY
        // ================================================================

        static void TestMishnaAvot(Tokenizer t)
        {
            var res = t.Extract("משה קיבל תורה מסיני ומסרה ליהושע ויהושע לזקנים וזקנים לנביאים");
            AssertContains(res, "משה");
            AssertContains(res, "תורה");
            AssertContains(res, "מסיני");
            AssertContains(res, "ליהושע");
            AssertContains(res, "לנביאים");
        }

        static void TestMishnaBerachot(Tokenizer t)
        {
            var res = t.Extract("מאימתי קורין את שמע בערבין משעה שהכהנים נכנסים לאכול בתרומתן");
            AssertContains(res, "מאימתי");
            AssertContains(res, "קורין");
            AssertContains(res, "שמע");
            AssertContains(res, "שהכהנים");
            AssertContains(res, "בתרומתן");
        }

        static void TestGemaraBavaMetzia(Tokenizer t)
        {
            var res = t.Extract("שנים אוחזין בטלית זה אומר אני מצאתיה וזה אומר אני מצאתיה");
            AssertContains(res, "שנים");
            AssertContains(res, "אוחזין");
            AssertContains(res, "בטלית");
            AssertContains(res, "מצאתיה");
            AssertContains(res, "זה");
            AssertContains(res, "וזה");
            AssertContains(res, "אומר");
            AssertContains(res, "אני");
        }

        static void TestGemaraShabbat(Tokenizer t)
        {
            var res = t.Extract("דעלך סני לחברך לא תעביד זו היא כל התורה כולה ואידך פירושה הוא זיל גמור");
            AssertContains(res, "דעלך");
            AssertContains(res, "לחברך");
            AssertContains(res, "התורה");
            AssertContains(res, "פירושה");
            AssertContains(res, "גמור");
        }

        static void TestMishnaAvot2(Tokenizer t)
        {
            var res = t.Extract("אל תאמין בעצמך עד יום מותך ואל תדין את חברך עד שתגיע למקומו");
            AssertContains(res, "תאמין");
            AssertContains(res, "בעצמך");
            AssertContains(res, "מותך");
            AssertContains(res, "חברך");
            AssertContains(res, "למקומו");
            AssertContains(res, "שתגיע");
        }

        // ================================================================
        // EDGE CASES
        // ================================================================

        static void TestFullNikudPassage(Tokenizer t)
        {
            var res = t.Extract("בְּרֵאשִׁית בָּרָא אֱלֹהִים אֵת הַשָּׁמַיִם וְאֵת הָאָרֶץ");
            AssertContains(res, "בראשית");
            AssertContains(res, "ברא");
            AssertContains(res, "אלהים");
            AssertContains(res, "השמים");
            AssertContains(res, "הארץ");
        }

        static void TestFinalLetterForms(Tokenizer t)
        {
            var res = t.Extract("מלך ארץ שלום");
            AssertContains(res, "מלך");
            AssertContains(res, "ארץ");
            AssertContains(res, "שלום");
        }

        static void TestMixedHebrewEnglish(Tokenizer t)
        {
            var res = t.Extract("The word שלום means peace and תורה means Torah");
            AssertContains(res, "the");
            AssertContains(res, "word");
            AssertContains(res, "שלום");
            AssertContains(res, "means");
            AssertContains(res, "peace");
            AssertContains(res, "תורה");
            AssertContains(res, "torah");
        }

        static void TestHebrewHtmlWrapped(Tokenizer t)
        {
            var res = t.Extract("<p class=\"verse\">בְּרֵאשִׁית <b>בָּרָא</b> אֱלֹהִים</p>");
            AssertContains(res, "בראשית");
            AssertContains(res, "ברא");
            AssertContains(res, "אלהים");
            AssertNotContains(res, "p");
            AssertNotContains(res, "b");
            AssertNotContains(res, "verse");
            AssertNotContains(res, "class");
        }

        static void TestHebrewEntitiesInline(Tokenizer t)
        {
            var res = t.Extract("שלו&shy;ם");
            AssertContains(res, "שלום");

            var res2 = t.Extract("שלום&nbsp;עולם");
            AssertContains(res2, "שלום");
            AssertContains(res2, "עולם");

            var res3 = t.Extract("שלום &nbsp; עולם");
            AssertContains(res3, "שלום");
            AssertContains(res3, "עולם");
        }

        static void TestDuplicateTermDedup(Tokenizer t)
        {
            var res = t.Extract("תורה תורה תורה ישראל ישראל");
            AssertContains(res, "תורה");
            AssertContains(res, "ישראל");
            AssertEqualCount(res, 2);
        }

        // ================================================================
        // ASSERT HELPERS
        // ================================================================

        static void AssertContains(HashSet<string> set, string value)
        {
            if (!set.Contains(value))
                Fail($"Missing expected token: '{value}'");
        }

        static void AssertNotContains(HashSet<string> set, string value)
        {
            if (set.Contains(value))
                Fail($"Unexpected token found: '{value}'");
        }

        static void AssertEqualCount(HashSet<string> set, int expected)
        {
            if (set.Count != expected)
                Fail($"Expected {expected} tokens but got {set.Count}: [{string.Join(", ", set)}]");
        }

        static void Fail(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("FAIL: " + msg);
            Console.ResetColor();
            Environment.Exit(1);
        }
    }
}
