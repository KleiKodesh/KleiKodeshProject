using System.Collections.Generic;

namespace FtsLib.Core
{
    /// <summary>
    /// A single word token produced by <see cref="TokenStream"/>.
    /// </summary>
    internal readonly struct TextToken
    {
        /// <summary>
        /// Index of the first letter of the word in the original raw string
        /// (before nikud was stripped). Points into the source HTML.
        /// </summary>
        public readonly int RawStart;

        /// <summary>
        /// Index just past the separator that ended the word in the original raw string.
        /// The actual last letter of the word is somewhere between RawStart and RawEnd.
        /// </summary>
        public readonly int RawEnd;

        /// <summary>
        /// Normalized form of the word: nikud stripped, ASCII lowercased.
        /// This is what you compare against query terms.
        /// </summary>
        public readonly string Normalized;

        public TextToken(int rawStart, int rawEnd, string normalized)
        {
            RawStart   = rawStart;
            RawEnd     = rawEnd;
            Normalized = normalized;
        }

        public override string ToString() => $"[{RawStart}–{RawEnd}] \"{Normalized}\"";
    }

    /// <summary>
    /// Produces a list of <see cref="TextToken"/> from an HTML string,
    /// preserving the raw character positions of each word alongside its
    /// normalized form. Used by the highlighter to locate match spans
    /// in the original source without a second pass.
    /// Not thread-safe — do not share across threads.
    /// </summary>
    internal sealed class TokenStream : HtmlTextScanner
    {
        private readonly List<TextToken> _tokens = new List<TextToken>();

        /// <summary>
        /// Tokenizes <paramref name="text"/> and returns all tokens in order.
        /// The returned list is reused on the next call — copy it if you need to keep it.
        /// </summary>
        public List<TextToken> Tokenize(string text)
        {
            _tokens.Clear();

            if (!string.IsNullOrEmpty(text))
                Scan(text);

            return _tokens;
        }

        protected override void OnWord(int rawStart, int rawEnd)
        {
            _tokens.Add(new TextToken(rawStart, rawEnd, _buffer.ToString()));
        }
    }
}
