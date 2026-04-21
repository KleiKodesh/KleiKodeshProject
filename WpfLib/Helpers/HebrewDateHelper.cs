namespace WpfLib.Helpers
{
    using System;
    using System.Globalization;
    using System.Text;

    public static class HebrewDateHelper
    {
        private static readonly HebrewCalendar _hebrewCalendar = new HebrewCalendar();
        private static readonly CultureInfo _jewishCulture = CreateJewishCulture();

        private static CultureInfo CreateJewishCulture()
        {
            var culture = CultureInfo.CreateSpecificCulture("he-IL");
            culture.DateTimeFormat.Calendar = new HebrewCalendar();
            return culture;
        }

        public static string GetHebrewDate(DateTime anyDate)
        {
            return GetHebrewDate(anyDate, false);
        }

        public static string GetHebrewDate(DateTime anyDate, bool addDayOfWeek)
        {
            StringBuilder hebrewFormattedString = new StringBuilder();

            if (addDayOfWeek)
            {
                // Day of the week (e.g., "יום שני")
                hebrewFormattedString.Append(anyDate.ToString("dddd", _jewishCulture)).Append(" ");
            }

            // Day of the month (e.g., "14")
            hebrewFormattedString.Append(anyDate.ToString("dd", _jewishCulture)).Append(" ");

            // Month and year (e.g., "אוגוסט 2025")
            hebrewFormattedString.Append(anyDate.ToString("y", _jewishCulture));

            return hebrewFormattedString.ToString();
        }

        public static string GetHebrewDateTime(DateTime date)
        {
            string datePart = GetHebrewDate(date);
            string hour = date.Hour.ToString().PadLeft(2, '0');
            string minute = date.Minute.ToString().PadLeft(2, '0');

            return $"{datePart} - {hour}:{minute}";
        }
    }
}
