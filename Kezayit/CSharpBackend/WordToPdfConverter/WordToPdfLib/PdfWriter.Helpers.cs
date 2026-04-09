using System;
using System.Collections.Generic;
using System.Linq;
using PdfSharp.Drawing;

namespace WordToPdfLib
{
    internal partial class PdfWriter
    {
        private List<string> WrapTextSimple(string text, XFont font, double maxWidth)
        {
            var lines   = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            var words   = text.Split(' ');
            var current = string.Empty;
            foreach (var word in words)
            {
                var test = string.IsNullOrEmpty(current) ? word : current + " " + word;
                if (_gfx.MeasureString(test, font).Width > maxWidth && !string.IsNullOrEmpty(current))
                {
                    lines.Add(current);
                    current = word;
                }
                else current = test;
            }
            if (!string.IsNullOrEmpty(current)) lines.Add(current);
            return lines;
        }

        private static string ToVisualRtl(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var words = text.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                var w = words[i];
                if (string.IsNullOrEmpty(w) || !w.Any(IsRtlChar)) continue;
                words[i] = MirrorPunctuation(new string(w.ToCharArray().Reverse().ToArray()));
            }
            Array.Reverse(words);
            return string.Join(" ", words);
        }

        private static string MirrorPunctuation(string s)
        {
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                switch (chars[i])
                {
                    case '(':      chars[i] = ')';      break;
                    case ')':      chars[i] = '(';      break;
                    case '[':      chars[i] = ']';      break;
                    case ']':      chars[i] = '[';      break;
                    case '{':      chars[i] = '}';      break;
                    case '}':      chars[i] = '{';      break;
                    case '<':      chars[i] = '>';      break;
                    case '>':      chars[i] = '<';      break;
                    case '\u201C': chars[i] = '\u201D'; break;
                    case '\u201D': chars[i] = '\u201C'; break;
                    case '\u2018': chars[i] = '\u2019'; break;
                    case '\u2019': chars[i] = '\u2018'; break;
                }
            }
            return new string(chars);
        }

        private static bool IsRtlChar(char c) =>
            (c >= '\u0590' && c <= '\u05FF') ||
            (c >= '\u0600' && c <= '\u06FF');
    }
}
