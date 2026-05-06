namespace FtsLib.Search
{
    /// <summary>
    /// Computes the Levenshtein (edit) distance between two strings.
    /// Uses a two-row DP approach — O(min(a,b)) space.
    /// </summary>
    internal static class Levenshtein
    {
        /// <summary>
        /// Returns the edit distance between <paramref name="a"/> and <paramref name="b"/>,
        /// stopping early and returning <paramref name="maxDistance"/> + 1 as soon as it
        /// is certain the true distance exceeds <paramref name="maxDistance"/>.
        /// </summary>
        public static int Distance(string a, string b, int maxDistance = int.MaxValue)
        {
            if (a == b)           return 0;
            if (a.Length == 0)    return b.Length;
            if (b.Length == 0)    return a.Length;

            // Keep the shorter string in 'a' to minimise row width
            if (a.Length > b.Length) { var tmp = a; a = b; b = tmp; }

            int lenA = a.Length;
            int lenB = b.Length;

            // If lengths differ by more than maxDistance, bail immediately
            if (lenB - lenA > maxDistance) return maxDistance + 1;

            var prev = new int[lenA + 1];
            var curr = new int[lenA + 1];

            for (int i = 0; i <= lenA; i++) prev[i] = i;

            for (int j = 1; j <= lenB; j++)
            {
                curr[0] = j;
                int rowMin = curr[0];

                for (int i = 1; i <= lenA; i++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[i] = Min3(
                        curr[i - 1] + 1,        // insert
                        prev[i]     + 1,        // delete
                        prev[i - 1] + cost);    // substitute
                    if (curr[i] < rowMin) rowMin = curr[i];
                }

                // Early exit: entire row exceeds maxDistance
                if (rowMin > maxDistance) return maxDistance + 1;

                var swap = prev; prev = curr; curr = swap;
            }

            return prev[lenA];
        }

        private static int Min3(int a, int b, int c)
            => a < b ? (a < c ? a : c) : (b < c ? b : c);
    }
}
