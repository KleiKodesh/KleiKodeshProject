using System.Collections.Generic;
using System.Text;

namespace BloomSearchEngineLib
{
    public sealed class TermExtractor
    {
        private readonly HashSet<string> _terms = new HashSet<string>();
        private readonly StringBuilder _word = new StringBuilder(64);

        public HashSet<string> ExtractTermsFromLines(List<string> lines)
        {
            _terms.Clear();
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    ProcessLine(line.NormalizeText()); // normalize first so indexed terms match search-time normalization
            }
            return _terms;
        }

        private void ProcessLine(string text)
        {
            bool inTag = false;
            int tagNamePos = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '<')
                {
                    if (i + 1 < text.Length && text[i + 1] >= '\u05D0' && text[i + 1] <= '\u05EA') { Flush(); continue; }
                    Flush(); inTag = true; tagNamePos = 0; continue;
                }

                if (inTag)
                {
                    if (c == '>') inTag = false;
                    tagNamePos++;
                    continue;
                }

                if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\u05BE' || c == '_') { Flush(); }
                else if (c >= '\u05D0' && c <= '\u05EA') _word.Append(c);
                else if (c >= 'A' && c <= 'Z') _word.Append((char)(c | 32));
                else if (c >= 'a' && c <= 'z') _word.Append(c);
            }
            Flush();
        }

        private void Flush() { if (_word.Length > 0) { _terms.Add(_word.ToString()); _word.Clear(); } }
    }
}
