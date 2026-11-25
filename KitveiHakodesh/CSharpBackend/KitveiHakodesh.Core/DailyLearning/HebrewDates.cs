using System.Globalization;
using System.Text;

namespace KitveiHakodesh.Core.DailyLearning;

/// <summary>
/// Hebrew dates as people write them ("כ״ה כסלו תשפ״ז") to and from a civil day key (yyyymmdd).
/// Reading is lenient (any geresh style or none, plene/defective month spellings, "אדר א" /
/// "אדר ראשון", year with or without the thousands); writing matches the widget's own format.
/// </summary>
public static class HebrewDates
{
    private static readonly HebrewCalendar Calendar = new();

    /// <summary>Month names in .NET's order for a common year (Tishrei = 1 ... Elul = 12).</summary>
    private static readonly string[] CommonMonths = ["תשרי", "חשון", "כסלו", "טבת", "שבט", "אדר", "ניסן", "אייר", "סיון", "תמוז", "אב", "אלול"];

    /// <summary>yyyy-MM-dd -> yyyymmdd; null when not a date.</summary>
    public static int? DayKey(string isoDate) =>
        DateTime.TryParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? Key(d) : null;

    private static int Key(DateTime d) => d.Year * 10000 + d.Month * 100 + d.Day;

    /// <summary>The Hebrew date of a day key, e.g. "כ״ה כסלו תשפ״ז".</summary>
    public static string Format(int dayKey)
    {
        var d = new DateTime(dayKey / 10000, dayKey / 100 % 100, dayKey % 100);
        int year = Calendar.GetYear(d), month = Calendar.GetMonth(d), day = Calendar.GetDayOfMonth(d);
        return $"{Letters(day)} {MonthName(year, month)} {Letters(year % 1000)}";
    }

    /// <summary>The day key of a written Hebrew date; null when the text is not one.</summary>
    public static int? Parse(string text)
    {
        var words = Regroup(text);
        if (words.Length < 3) return null;
        string yearWord = words[^1];
        // "התשפז": the leading ה is the thousands (5000).
        bool thousands = yearWord.Length > 2 && yearWord[0] == 'ה';
        if (Value(words[0]) is not int day || Value(thousands ? yearWord[1..] : yearWord) is not int year) return null;
        if (year < 1000) year += 5000;
        string monthText = string.Join(' ', words[1..^1]);
        if (MonthNumber(monthText, year) is not int month) return null;
        if (year < 5001 || year > 6999 || month > Calendar.GetMonthsInYear(year) || day < 1 || day > Calendar.GetDaysInMonth(year, month)) return null;
        try { return Key(Calendar.ToDateTime(year, month, day, 0, 0, 0, 0)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>Words with geresh marks removed from inside them (כ״ה -> כה), split on spaces.</summary>
    private static string[] Regroup(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text.Trim())
            if (c is not ('׳' or '״' or '\'' or '"' or '’' or '”')) sb.Append(c);
        return sb.ToString().Replace('־', ' ').Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string MonthName(int year, int month)
    {
        if (!Calendar.IsLeapYear(year)) return CommonMonths[month - 1];
        return month switch
        {
            6 => "אדר א׳",
            7 => "אדר ב׳",
            < 6 => CommonMonths[month - 1],
            _ => CommonMonths[month - 2],
        };
    }

    private static int? MonthNumber(string text, int year)
    {
        // Plene / defective / long forms -> the short spelling.
        // "בכסלו": the ב is "in", not part of the month.
        if (text.Length > 2 && text[0] == 'ב' && !text.StartsWith("בשבט", StringComparison.Ordinal)) text = text[1..];
        string m = text.Replace("מרחשון", "חשון").Replace("מרחשוון", "חשון").Replace("חשוון", "חשון").Replace("סיוון", "סיון")
            .Replace("ראשון", "א").Replace("שני", "ב").Trim();
        bool leap = Calendar.IsLeapYear(year);
        if (m is "אדר א" or "אדרא") return leap ? 6 : null;
        if (m is "אדר ב" or "אדרב") return leap ? 7 : null;
        if (m == "אדר") return leap ? 7 : 6; // plain Adar in a leap year: Adar II, as the calendar reads it
        int index = Array.IndexOf(CommonMonths, m);
        if (index < 0) return null;
        int month = index + 1;
        return leap && month > 6 ? month + 1 : month;
    }

    private static readonly Dictionary<char, int> LetterValues = new()
    {
        ['א'] = 1, ['ב'] = 2, ['ג'] = 3, ['ד'] = 4, ['ה'] = 5, ['ו'] = 6, ['ז'] = 7, ['ח'] = 8, ['ט'] = 9,
        ['י'] = 10, ['כ'] = 20, ['ך'] = 20, ['ל'] = 30, ['מ'] = 40, ['ם'] = 40, ['נ'] = 50, ['ן'] = 50, ['ס'] = 60,
        ['ע'] = 70, ['פ'] = 80, ['ף'] = 80, ['צ'] = 90, ['ץ'] = 90, ['ק'] = 100, ['ר'] = 200, ['ש'] = 300, ['ת'] = 400,
    };

    /// <summary>A gematria number (letters only) or plain digits; null otherwise.</summary>
    private static int? Value(string word)
    {
        if (int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out int digits)) return digits;
        int sum = 0;
        foreach (char c in word)
        {
            if (!LetterValues.TryGetValue(c, out int v)) return null;
            sum += v;
        }
        return sum > 0 ? sum : null;
    }

    /// <summary>1..999 as gematria with geresh / gershayim (15 = ט״ו, 16 = ט״ז).</summary>
    private static string Letters(int n)
    {
        var sb = new StringBuilder();
        for (; n >= 400; n -= 400) sb.Append('ת');
        foreach (var (value, letter) in new[] { (300, 'ש'), (200, 'ר'), (100, 'ק') })
            if (n >= value) { sb.Append(letter); n -= value; }
        if (n == 15 || n == 16) { sb.Append('ט').Append(n == 15 ? 'ו' : 'ז'); n = 0; }
        foreach (var (value, letter) in new[] { (90, 'צ'), (80, 'פ'), (70, 'ע'), (60, 'ס'), (50, 'נ'), (40, 'מ'), (30, 'ל'), (20, 'כ'), (10, 'י') })
            if (n >= value) { sb.Append(letter); n -= value; }
        if (n > 0) sb.Append("אבגדהוזחט"[n - 1]);
        return sb.Length == 1 ? sb.Append('׳').ToString() : sb.Insert(sb.Length - 1, '״').ToString();
    }
}
