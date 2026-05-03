using System;
using System.IO;

namespace FtsEngine.Core
{
    /// <summary>
    /// Merges all sorted run files into the final index:
    ///   postings.bin  — compressed posting lists (delta+varint)
    ///   index.db      — term → (offset, length, count)
    ///
    /// Deletes run files after a successful merge.
    /// </summary>
    public static class IndexWriter
    {
        public static void Merge(string[] runPaths, string postingsPath, string indexDbPath,
                                  Action onDictionaryWrite = null,
                                  Action<int> onMergeProgress = null) // fires every 100k terms
        {
            if (File.Exists(postingsPath)) File.Delete(postingsPath);
            if (File.Exists(indexDbPath))  File.Delete(indexDbPath);

            using (var postings = new FileStream(postingsPath, FileMode.Create,
                                                 FileAccess.Write, FileShare.None,
                                                 4 * 1024 * 1024))
            using (var dict    = new TermDictionary(indexDbPath))
            using (var merger  = new RunMerger(runPaths))
            {
                int termCount = 0;
                const int progressInterval = 100_000;

                while (merger.HasMore)
                {
                    var (term, bytes, count, _) = merger.Next();
                    long offset = postings.Position;
                    postings.Write(bytes, 0, bytes.Length);
                    dict.Add(term, offset, bytes.Length, count);

                    if (onMergeProgress != null && ++termCount % progressInterval == 0)
                        onMergeProgress(termCount);
                }

                onDictionaryWrite?.Invoke();
                dict.Commit();
            }

            // Clean up run files
            foreach (var path in runPaths)
                if (File.Exists(path)) File.Delete(path);
        }
    }
}
