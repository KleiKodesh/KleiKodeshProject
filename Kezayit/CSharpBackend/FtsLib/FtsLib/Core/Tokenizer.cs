using System.Collections.Generic;

namespace FtsLib.Core
{
    /// <summary>
    /// Extracts the set of unique normalized terms from an HTML string.
    /// Strips nikud, lowercases ASCII, ignores HTML tags and non-letter characters.
    /// Not thread-safe — do not share across threads.
    /// </summary>
    public sealed class Tokenizer : HtmlTextScanner
    {
        private readonly HashSet<string> _terms = new HashSet<string>();

        /// <summary>
        /// Returns the set of unique normalized terms found in <paramref name="text"/>.
        /// The returned set is reused on the next call — copy it if you need to keep it.
        /// </summary>
        public HashSet<string> Extract(string text)
        {
            _terms.Clear();

            if (!string.IsNullOrEmpty(text))
                Scan(text);

            return _terms;
        }

        protected override void OnWord(int rawStart, int rawEnd)
        {
            // rawStart / rawEnd are available but not needed here —
            // the Tokenizer only cares about the normalized form.
            _terms.Add(_buffer.ToString());
        }
    }
}
