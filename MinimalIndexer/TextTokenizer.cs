using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace MinimalIndexer
{
    internal class TextTokenizer
    {
        const int MinWordLength = 2, MaxWordLength = 44;

        readonly string _text;
        readonly HashSet<string> _tokens;
        readonly StringBuilder _sb = new StringBuilder(48);
        int index;

        internal HashSet<string> Tokens => _tokens;


        internal TextTokenizer(string text)
        {
            _text = text;
            int estimatedWords = Math.Max(16, text.Length / 4);
            _tokens = new HashSet<string>(estimatedWords, StringComparer.Ordinal);
            Tokenize();
        }

        void Tokenize()
        {
            while (index < _text.Length)
            {
                char c = _text[index];
                if (IsHebrewLetter(c))
                    ReadWord();
                else if (c == '<')
                    SkipHtmlTag();
                else
                    index++;
            }
        }

        void ReadWord()
        {
            _sb.Clear();

            while (index < _text.Length)
            {
                char c = _text[index];

                if (IsHebrewLetter(c))
                {
                    _sb.Append(c);
                    index++;
                }
                else if (c == '<')
                {
                    SkipHtmlTag();
                }
                else if (IsHebrewDiacritic(c) || c == '\"')
                {
                    index++;
                }
                else if (IsMaqaf(c))
                {
                    // word boundary
                    index++; // consume maqaf
                    break;
                }
                else
                {
                    break;
                }
            }

            AddWord();
        }


        void AddWord()
        {
            int length = _sb.Length;
            if (length >= MinWordLength && length <= MaxWordLength)
            {
                var stems = SmartStemmer.Generate(_sb.ToString());
                _tokens.UnionWith(stems);
                //_tokens.Add(_sb.ToString());
            }
        }

        void SkipHtmlTag()
        {
            index++;
            while (index < _text.Length && _text[index] != '>')
                index++;
            if (index < _text.Length)
                index++;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsHebrewLetter(char c)
            => c >= 'א' && c <= 'ת';

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsHebrewDiacritic(char c)
        {
            return c >= '\u0591' && c <= '\u05C7' && c != '\u05BE';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsMaqaf(char c) => c == '\u05BE';
    }
}