using System.Collections.Generic;
using System.Text;

namespace FtsLib.Tokenization
{
    /// <summary>
    /// Extracts the set of unique normalized terms from an HTML string.
    /// Strips nikud, lowercases ASCII, ignores HTML tags and non-letter characters.
    /// Not thread-safe — do not share across threads.
    /// </summary>
    internal sealed class Tokenizer : HtmlWordScanner
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

        protected override void OnWord(int rawStart, int rawEnd, int visibleStart)
        {
            // rawStart / rawEnd / visibleStart are available but not needed here —
            // the Tokenizer only cares about the normalized form.
            string term = _buffer.ToString();
            // Otzaria's RemoveLongFilter: a term over MaxTermBytes (UTF-8, its inner quotes
            // counting one byte each, as engines that keep them store it) is not indexed.
            if (Encoding.UTF8.GetByteCount(term) + WordInnerQuotes > HebrewChars.MaxTermBytes) return;
            _terms.Add(term);
        }
    }
}
