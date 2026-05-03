using System.Collections.Generic;

namespace FtsLib
{
    /// <summary>
    /// Common Hebrew and Aramaic stop words that appear in almost every line
    /// and carry no search value. Filtering these reduces index size and
    /// speeds up both indexing and search significantly.
    /// </summary>
    public static class StopWords
    {
        private static readonly HashSet<string> _words = new HashSet<string>(
            System.StringComparer.Ordinal)
        {
            // ---- Hebrew prepositions / conjunctions / articles ----
            "את", "של", "על", "אל", "כי", "לא", "כל", "עם", "הם",
            "הן", "הוא", "היא", "אני", "אנו", "אנחנו", "אתה", "אתם",
            "הם", "הן", "זה", "זו", "זאת", "אשר", "אם", "כן", "לו",
            "לה", "לי", "לנו", "לכם", "להם", "להן", "בו", "בה", "בי",
            "בנו", "בכם", "בהם", "בהן", "מן", "מי", "מה", "שם", "כך",
            "כן", "גם", "רק", "עד", "אך", "אבל", "או", "כבר", "עוד",
            "יש", "אין", "כן", "לכן", "כאשר", "אחר", "אחרי", "לפני",
            "בין", "תחת", "מתחת", "מעל", "אצל", "ליד", "דרך", "בגלל",
            "למען", "בעד", "נגד", "בלי", "חוץ", "פן", "שלא", "כדי",
            "כדי", "כמו", "כמוהו", "כמוה", "כמוני", "כמונו",

            // ---- Hebrew definite article prefixes (common standalone tokens) ----
            "ה", "ו", "ב", "כ", "ל", "מ", "ש",

            // ---- Very common biblical/talmudic words ----
            "ויאמר", "ויהי", "והיה", "אמר", "אמרו", "דבר", "דברי",
            "בני", "בנו", "בנה", "בית", "ביד", "ביום", "בשם",
            "לאמר", "לכל", "לכם", "לנו", "לו", "לה", "לי",
            "כאשר", "אשר", "אשר", "אחד", "אחת",

            // ---- Aramaic stop words (Talmud) ----
            "דא", "דין", "הא", "הכי", "הכא", "התם", "אמר", "אמרי",
            "אמרינן", "קאמר", "קאמרי", "מאי", "היכי", "כיון", "דהא",
            "דכי", "דלא", "ולא", "ואי", "אי", "כי", "לה", "ליה",
            "להו", "מיניה", "מינה", "גביה", "גבה", "בהדיה", "בהדה",
            "עליה", "עלה", "עלייהו", "קמיה", "קמה", "קמייהו",
            "דידיה", "דידה", "דידהו", "נמי", "נמי", "תו", "אלא",
            "אלמא", "אלמה", "היינו", "הוא", "היא", "הני", "הנהו",
            "ההוא", "ההיא", "כולהו", "כולה", "כולי", "כולא",
            "מידי", "מידי", "שפיר", "בעי", "בעא", "בעו",
        };

        public static bool IsStopWord(string term) => _words.Contains(term);
    }
}
