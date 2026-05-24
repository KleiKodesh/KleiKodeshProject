using System.Collections.Generic;
using System.Text;

namespace Nakdan.Core
{
    public static class TokenTextConverter
    {
        public static string ToPlainText(
            List<Token> tokens)
        {
            var sb = new StringBuilder(tokens.Count);

            foreach (Token t in tokens)
                sb.Append(t.Base);

            return sb.ToString();
        }

        public static void FillVowels(
            List<Token> tokens,
            string vowelized)
        {
            int tokenIdx = 0;
            int lastBaseTokenIdx = -1;

            foreach (char c in vowelized)
            {
                if (c.IsNikkudChar())
                {
                    if (lastBaseTokenIdx >= 0)
                    {
                        tokens[lastBaseTokenIdx]
                            .VowelsAfter += c;
                    }
                }
                else
                {
                    if (tokenIdx < tokens.Count)
                    {
                        lastBaseTokenIdx = tokenIdx;
                        tokenIdx++;
                    }
                }
            }
        }
    }
}
