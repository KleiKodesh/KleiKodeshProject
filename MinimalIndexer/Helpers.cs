using System.Collections.Generic;

namespace MinimalIndexer
{
    internal static class Helpers
    {
        public static IEnumerable<T[]> Chunk<T>(IEnumerable<T> src, int size)
        {
            if (src is IList<T> list)
            {
                int count = list.Count;

                for (int i = 0; i < count; i += size)
                {
                    int len = size;
                    if (i + len > count) len = count - i;

                    var chunk = new T[len];
                    for (int j = 0; j < len; j++)
                        chunk[j] = list[i + j];

                    yield return chunk;
                }
                yield break;
            }

            // Fallback for true IEnumerable
            var buffer = new T[size];
            int index = 0;

            foreach (var item in src)
            {
                buffer[index++] = item;

                if (index == size)
                {
                    yield return buffer;
                    buffer = new T[size];
                    index = 0;
                }
            }

            if (index > 0)
            {
                var last = new T[index];
                for (int i = 0; i < index; i++)
                    last[i] = buffer[i];

                yield return last;
            }
        }
    }
}
