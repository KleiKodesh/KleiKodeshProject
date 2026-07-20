using System;
using System.Collections.Generic;

namespace FtsLib.Tokenization
{
    /// <summary>
    /// A single word token produced by <see cref="TokenStream"/>.
    ///
    /// The normalized form is stored as a slice into the producing stream's shared, reused char
    /// buffer (NOT a per-token string) — <see cref="NormSpan"/> exposes it allocation-free for
    /// the hot snippet path. <see cref="Normalized"/> still returns a string, allocated lazily on
    /// access, for callers that need one. Both views are only valid until the next
    /// <see cref="TokenStream.Tokenize"/> call (the buffer is reused) — the same lifetime as the
    /// returned token list.
    /// </summary>
    internal readonly struct TextToken
    {
        /// <summary>Index of the first letter of the word in the original raw string.</summary>
        public readonly int RawStart;

        /// <summary>Index just past the separator that ended the word in the original raw string.</summary>
        public readonly int RawEnd;

        /// <summary>
        /// Cumulative count of visible characters up to but not including the first letter of
        /// this token. Used by SnippetBuilder to measure snippet length without re-scanning.
        /// </summary>
        public readonly int VisibleStart;

        // Normalized form = _norm[NormStart .. NormStart+NormLen). _norm is the stream's reused buffer.
        private readonly char[] _norm;
        public readonly int NormStart;
        public readonly int NormLen;

        /// <summary>Normalized form as a span into the stream buffer — allocation-free.</summary>
        public ReadOnlySpan<char> NormSpan => _norm.AsSpan(NormStart, NormLen);

        /// <summary>
        /// Normalized form as a string (nikud stripped, ASCII lowercased). Allocated lazily on
        /// access — prefer <see cref="NormSpan"/> on hot paths.
        /// </summary>
        public string Normalized => new string(_norm, NormStart, NormLen);

        public TextToken(int rawStart, int rawEnd, char[] norm, int normStart, int normLen, int visibleStart)
        {
            RawStart     = rawStart;
            RawEnd       = rawEnd;
            _norm        = norm;
            NormStart    = normStart;
            NormLen      = normLen;
            VisibleStart = visibleStart;
        }

        public override string ToString() => $"[{RawStart}–{RawEnd}] \"{Normalized}\"";
    }

    /// <summary>
    /// Produces a list of <see cref="TextToken"/> from an HTML string, preserving each word's raw
    /// character positions alongside its normalized form. The normalized text of every token is
    /// packed into one reused char buffer (sized once per call to the input length — an upper
    /// bound, since normalization only drops/lowercases), so tokenizing a line allocates no
    /// per-token strings. Used by the highlighter to locate match spans in the original source.
    /// Not thread-safe — do not share across threads.
    /// </summary>
    internal sealed class TokenStream : HtmlWordScanner
    {
        private readonly List<TextToken> _tokens = new List<TextToken>();
        private char[] _normBuf = new char[256];
        private int    _normLen;

        /// <summary>
        /// Tokenizes <paramref name="text"/> and returns all tokens in order. The returned list
        /// AND every token's normalized view are reused on the next call — copy what you keep.
        /// </summary>
        public List<TextToken> Tokenize(string text)
        {
            _tokens.Clear();
            _normLen = 0;

            if (!string.IsNullOrEmpty(text))
            {
                if (_normBuf.Length < text.Length)
                    _normBuf = new char[text.Length]; // normalized length <= raw length; grow-only, reused
                Scan(text);
            }

            return _tokens;
        }

        protected override void OnWord(int rawStart, int rawEnd, int visibleStart)
        {
            int len = _buffer.Length;
            _buffer.CopyTo(0, _normBuf, _normLen, len);
            _tokens.Add(new TextToken(rawStart, rawEnd, _normBuf, _normLen, len, visibleStart));
            _normLen += len;
        }
    }
}
