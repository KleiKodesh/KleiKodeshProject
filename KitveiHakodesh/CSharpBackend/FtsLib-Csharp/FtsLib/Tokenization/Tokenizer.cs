using System.Collections.Generic;

namespace FtsLib.Tokenization
{
    /// <summary>
    /// Extracts the set of unique normalized terms from an HTML string.
    /// Strips nikud, lowercases, ignores HTML tags and non-letter characters.
    /// Not thread-safe - do not share across threads.
    /// </summary>
    internal sealed class Tokenizer : HtmlWordScanner
    {
        private readonly HashSet<string> _terms = new HashSet<string>();

        /// <summary>
        /// Returns the set of unique normalized terms found in <paramref name="text"/>.
        /// The returned set is reused on the next call - copy it if you need to keep it.
        /// </summary>
        public HashSet<string> Extract(string text)
        {
            _terms.Clear();

            if (!string.IsNullOrEmpty(text))
                Scan(text);

            return _terms;
        }

        protected override void OnWord(int rawStart, int rawEnd, int visibleStart, int position)
        {
            // A word whose term is too long is not a term (Otzaria's RemoveLongFilter: it keeps
            // len < 128 of its quote-including term); it still keeps its position.
            if (!HebrewChars.IsIndexableLength(WordTermBytes)) return;
            _terms.Add(_buffer.ToString());
        }
    }
}
