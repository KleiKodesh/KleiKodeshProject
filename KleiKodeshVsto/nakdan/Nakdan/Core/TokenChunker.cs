using System.Collections.Generic;

namespace Nakdan.Core
{
    public static class TokenChunker
    {
        public static List<List<Token>> Chunk(
            List<Token> tokens,
            int maxChars)
        {
            var chunks = new List<List<Token>>();
            var current = new List<Token>();

            int len = 0;

            foreach (Token tok in tokens)
            {
                if (len >= maxChars)
                {
                    int rb = current.Count - 1;

                    while (rb > 0
                        && !char.IsWhiteSpace(current[rb].Base))
                    {
                        rb--;
                    }

                    if (rb > 0)
                    {
                        var overflow = current.GetRange(
                            rb + 1,
                            current.Count - rb - 1);

                        current.RemoveRange(
                            rb + 1,
                            current.Count - rb - 1);

                        chunks.Add(current);

                        current = overflow;
                        len = current.Count;
                    }
                    else
                    {
                        chunks.Add(current);

                        current = new List<Token>();
                        len = 0;
                    }
                }

                current.Add(tok);
                len++;
            }

            if (current.Count > 0)
                chunks.Add(current);

            return chunks;
        }
    }
}
