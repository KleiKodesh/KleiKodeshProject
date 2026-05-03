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
    internal static class IndexWriter
    {
        public static void Merge(string[] runPaths, string postingsPath, string indexDbPath,
                                  Action onDictionaryWrite = null)
        {
            if (File.Exists(postingsPath)) File.Delete(postingsPath);
            if (File.Exists(indexDbPath))  File.Delete(indexDbPath);

            using (var postings = new FileStream(postingsPath, FileMode.Create,
                                                 FileAccess.Write, FileShare.None,
                                                 4 * 1024 * 1024))
            using (var dict    = new TermDictionary(indexDbPath))
            using (var merger  = new RunMerger(runPaths))
            {
                var plBuf = new PostingListBuffer();
                string lastTerm = null;

                while (merger.HasMore)
                {
                    var (term, docId) = merger.Next();

                    if (term != lastTerm)
                    {
                        // Flush previous term
                        if (lastTerm != null)
                        {
                            var (off, len, cnt) = plBuf.Flush(postings);
                            dict.Add(lastTerm, off, len, cnt);
                        }

                        plBuf.Begin(term);
                        lastTerm = term;
                    }

                    plBuf.Add(docId);
                }

                // Flush final term
                if (lastTerm != null)
                {
                    var (off, len, cnt) = plBuf.Flush(postings);
                    dict.Add(lastTerm, off, len, cnt);
                }

                dict.Commit();
                onDictionaryWrite?.Invoke();
            }

            // Clean up run files
            foreach (var path in runPaths)
                if (File.Exists(path)) File.Delete(path);
        }
    }
}
