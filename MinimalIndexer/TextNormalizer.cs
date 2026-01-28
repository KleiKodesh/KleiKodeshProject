namespace MinimalIndexer
{
    using System;
    using System.Text;

    internal static class TextNormalizer
    {
        // Thread-local reusable StringBuilder to avoid allocations
        [ThreadStatic]
        private static StringBuilder _sb;

        /// <summary>
        /// Removes HTML tags and diacritics from text in a single iteration
        /// </summary>
        internal static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text ?? string.Empty;

            // Reuse StringBuilder
            if (_sb == null)
                _sb = new StringBuilder(1024);
            else
                _sb.Clear();

            if (_sb.Capacity < text.Length)
                _sb.Capacity = text.Length;

            bool insideTag = false;

            foreach (char c in text)
            {
                if (c == '<')
                {
                    insideTag = true;
                    continue;
                }
                else if (c == '>')
                {
                    insideTag = false;
                    continue;
                }

                if (insideTag)
                    continue;

                // Replace maqaf with space
                if (c == '\u05BE')
                {
                    _sb.Append(' ');
                    continue;
                }

                // Remove niqqud (Hebrew diacritics range: 1425-1487)
                if (c < 1425 || c > 1487)
                    _sb.Append(char.ToLowerInvariant(c));
            }

            return _sb.ToString();
        }
    }
}