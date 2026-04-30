using System;
using System.Text;

namespace LuceneIndexBenchmark
{
    /// <summary>
    /// Strips Hebrew diacritics (nikud, teamim, punctuation marks) from text
    /// and lowercases ASCII so that variant spellings index to the same tokens.
    /// Mirrors the normalization applied by the Bloom filter pipeline.
    /// </summary>
    public static class HebrewTextNormalizer
    {
        // Unicode ranges to strip:
        //   U+0591–U+05C7  Hebrew diacritics (nikud, teamim, dagesh, etc.)
        //   U+FB1E         Hebrew point Judeo-Spanish Varika
        //   U+05F3–U+05F4  Hebrew punctuation (geresh, gershayim)
        private const char NikudStart = '\u0591';
        private const char NikudEnd   = '\u05C7';
        private const char Varika     = '\uFB1E';
        private const char Geresh     = '\u05F3';
        private const char Gershayim  = '\u05F4';

        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var builder = new StringBuilder(text.Length);
            foreach (char character in text)
            {
                // Strip nikud / teamim range
                if (character >= NikudStart && character <= NikudEnd)
                    continue;
                // Strip Varika
                if (character == Varika)
                    continue;
                // Strip geresh / gershayim (used as abbreviation marks in Hebrew)
                if (character == Geresh || character == Gershayim)
                    continue;
                // Lowercase ASCII
                builder.Append(char.ToLowerInvariant(character));
            }
            return builder.ToString();
        }

        /// <summary>
        /// Splits normalized text into individual word tokens.
        /// Splits on any non-Hebrew, non-ASCII-letter character.
        /// </summary>
        public static string[] Tokenize(string normalizedText)
        {
            if (string.IsNullOrEmpty(normalizedText))
                return Array.Empty<string>();

            var tokens = new System.Collections.Generic.List<string>();
            int start = -1;

            for (int index = 0; index <= normalizedText.Length; index++)
            {
                bool isWordChar = index < normalizedText.Length && IsWordCharacter(normalizedText[index]);

                if (isWordChar && start == -1)
                {
                    start = index;
                }
                else if (!isWordChar && start != -1)
                {
                    string token = normalizedText.Substring(start, index - start);
                    if (token.Length > 1) // skip single-character tokens
                        tokens.Add(token);
                    start = -1;
                }
            }

            return tokens.ToArray();
        }

        private static bool IsWordCharacter(char character)
        {
            // Hebrew letters: U+05D0–U+05EA
            if (character >= '\u05D0' && character <= '\u05EA')
                return true;
            // Hebrew extended letters (final forms etc.): U+FB1D–U+FB4E
            if (character >= '\uFB1D' && character <= '\uFB4E')
                return true;
            // ASCII letters and digits
            if ((character >= 'a' && character <= 'z') ||
                (character >= 'A' && character <= 'Z') ||
                (character >= '0' && character <= '9'))
                return true;
            return false;
        }
    }
}
