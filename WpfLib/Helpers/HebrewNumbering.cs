using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WpfLib.Helpers
{
    public static class HebrewNumbering
    {
        static Dictionary<int, string> HebNumbers = new Dictionary<int, String>
        {

        {400, "ת"},

        {300, "ש"},

        {200, "ר"},

        {100, "ק"},

        {90, "צ"},

        {80, "פ"},

        {70, "ע"},

        {60, "ס"},

        {50, "נ"},

        {40, "מ"},

        {30, "ל"},

        {20, "כ"},

        {19, "יט"},

        {18, "יח"},

        {17, "יז"},

        {16, "טז"},

        {15, "טו"},

        {10, "י"},

        {9, "ט"},

        {8, "ח"},

        {7, "ז"},

        {6, "ו"},

        {5, "ה"},

        {4, "ד"},

        {3, "ג"},

        {2, "ב"},

        {1, "א"},

        };





        public static int ToNumber(this string numHeb)

        {

            int last = 0;

            int total = 0;



            while (numHeb.Length > 0)

            {

                var next = HebNumbers.Where(v => numHeb.EndsWith(v.Value)).OrderBy(v => v.Key).LastOrDefault();

                if (next.Key <= last)

                    return -1;



                last = next.Key;

                total += last;

                numHeb = numHeb.Remove(numHeb.Length - next.Value.Length);

            }



            return total;

        }



        public static string ToHebNumber(this int num)
        {
            if (num <= 0)
                return "";

            string hebrewNumber = "";

            // Handle thousands
            if (num >= 1000)
            {
                int thousands = num / 1000;
                num %= 1000;

                // Append thousands with a geresh
                hebrewNumber += ToHebNumber(thousands) + "׳";
            }

            while (num > 0)
            {
                var key = HebNumbers
                    .Where(pair => pair.Key <= num)
                    .OrderByDescending(pair => pair.Key)
                    .First();

                hebrewNumber += key.Value;
                num -= key.Key;
            }

            // Correct special cases
            hebrewNumber = hebrewNumber.Replace("יה", "טו");
            hebrewNumber = hebrewNumber.Replace("יו", "טז");

            // Optional corrections (feel free to comment these out if undesired)
            hebrewNumber = hebrewNumber.Replace("רעב", "ערב");
            hebrewNumber = hebrewNumber.Replace("רעד", "עדר");
            hebrewNumber = hebrewNumber.Replace("רע", "ער");
            hebrewNumber = hebrewNumber.Replace("רצח", "רחצ");
            hebrewNumber = hebrewNumber.Replace("תשמד", "תדשם");
            hebrewNumber = hebrewNumber.Replace("שמד", "שדמ");
            hebrewNumber = hebrewNumber.Replace("שד", "דש");

            return hebrewNumber;
        }

    }
}
