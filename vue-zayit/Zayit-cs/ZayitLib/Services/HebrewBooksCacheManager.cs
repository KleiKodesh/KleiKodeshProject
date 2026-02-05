using System;
using System.IO;
using System.Linq;

namespace Zayit.Services
{
    /// <summary>
    /// Cache Manager specifically for Hebrew Books PDFs
    /// Manages up to MAX_FILES cached Hebrew book PDFs with LRU eviction
    /// </summary>
    public class HebrewBooksCacheManager
    {
        private const int MAX_FILES = 10;
        private readonly string _cacheDir;
        private readonly object _lock = new object();

        public HebrewBooksCacheManager(string htmlPath)
        {
            _cacheDir = Path.Combine(htmlPath, "pdfjs", "web", "hebrewbookscache");
            Directory.CreateDirectory(_cacheDir);
        }

        /// <summary>
        /// Enforce the maximum file limit using LRU eviction
        /// Called when a new file is added to cache
        /// </summary>
        public void EnforceFileLimit()
        {
            lock (_lock)
            {
                try
                {
                    var files = Directory.GetFiles(_cacheDir, "*.pdf")
                        .Select(f => new FileInfo(f))
                        .OrderBy(f => f.LastAccessTime)
                        .ToList();

                    // Remove oldest files if we exceed the limit
                    while (files.Count >= MAX_FILES)
                    {
                        var oldest = files.First();
                        try
                        {
                            oldest.Delete();
                            Console.WriteLine($"[HebrewBooksCacheManager] Evicted old file: {oldest.Name}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[HebrewBooksCacheManager] Error evicting file {oldest.Name}: {ex.Message}");
                        }
                        files.RemoveAt(0);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HebrewBooksCacheManager] Error enforcing file limit: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public object GetStats()
        {
            lock (_lock)
            {
                try
                {
                    var files = Directory.GetFiles(_cacheDir, "*.pdf");
                    var totalSize = files.Sum(f => new FileInfo(f).Length);

                    return new
                    {
                        totalFiles = files.Length,
                        totalSizeMB = Math.Round(totalSize / (1024.0 * 1024.0), 1),
                        maxFiles = MAX_FILES
                    };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HebrewBooksCacheManager] Error getting stats: {ex.Message}");
                    return new { totalFiles = 0, totalSizeMB = 0.0, maxFiles = MAX_FILES };
                }
            }
        }

        /// <summary>
        /// Clear all cached Hebrew book files
        /// </summary>
        public void ClearAll()
        {
            lock (_lock)
            {
                try
                {
                    var files = Directory.GetFiles(_cacheDir, "*.pdf");
                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[HebrewBooksCacheManager] Error deleting file {file}: {ex.Message}");
                        }
                    }

                    Console.WriteLine($"[HebrewBooksCacheManager] Cleared {files.Length} Hebrew book files");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HebrewBooksCacheManager] Error clearing cache: {ex.Message}");
                }
            }
        }
    }
}