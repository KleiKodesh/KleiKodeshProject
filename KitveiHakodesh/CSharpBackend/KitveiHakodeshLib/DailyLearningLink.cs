using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace KitveiHakodeshLib
{
    /// <summary>
    /// A link to one daily-learning cycle's reading for a day, in this app's own scheme:
    ///
    ///   kitveihakodeshapp://daily-learning/&lt;cycle&gt;[?date=YYYY-MM-DD]
    ///
    /// The cycle is the frontend's key (homeDailyLearningTypes.ts: daf-yomi, rambam-1, ...);
    /// no date = today. The standalone calendar opens these, so a click on a cycle there
    /// opens its book in this app. The frontend's utils/dailyLearningLink.ts writes them -
    /// change both together.
    /// </summary>
    public sealed class DailyLearningLink
    {
        /// <summary>The cycle's key; letters, digits and dashes only.</summary>
        public string Cycle { get; private set; }

        /// <summary>The day as YYYY-MM-DD; null = today.</summary>
        public string Date { get; private set; }

        private static readonly Regex LinkRe = new Regex(
            @"^(?:" + HostLink.AppScheme + "|" + HostLink.LegacyAppScheme + @")://daily-learning/([a-z0-9-]+)/?(?:\?date=(\d{4}-\d{2}-\d{2}))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Returns null unless the URL is a daily-learning link with a valid date (if any).</summary>
        public static DailyLearningLink TryParse(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            Match match = LinkRe.Match(url.Trim());
            if (!match.Success) return null;
            string date = match.Groups[2].Success ? match.Groups[2].Value : null;
            if (date != null && !DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return null;
            return new DailyLearningLink { Cycle = match.Groups[1].Value.ToLowerInvariant(), Date = date };
        }
    }
}
