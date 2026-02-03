using System;
using System.Collections.Generic;
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
        private readonly HashSet<string> _activeFiles = new HashSet<string>();
        private readonly object _lock = new object();

        public HebrewBooksCacheManager(string htmlPath)
        {
            _cacheDir = Path.Combine(htmlPath, "pdfjs", "web", "hebrewbookscache");
            Directory.CreateDirectory(_cacheDir);
        }

        /// <summary>
        /// Register a Hebrew book file as active (prevents deletion)
        /// </summary>
        public void RegisterActive(string fileName)
        {
            lock (_lock)
            {
                _activeFiles.Add(fileName);
                EnforceFileLimit();
            }
        }

        /// <summary>
        /// Unregister a Hebrew book file as active (allows deletion)
        /// </summary>
        public void UnregisterActive(string fileName)
        {
            lock (_lock)
            {
                if (_activeFiles.Remove(fileName))
                {
                    TryDeleteFile(fileName);
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
                        activeFiles = _activeFiles.Count,
                        totalSizeMB = Math.Round(totalSize / (1024.0 * 1024.0), 1),
                        maxFiles = MAX_FILES
                    };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HebrewBooksCacheManager] Error getting stats: {ex.Message}");
                    return new { totalFiles = 0, activeFiles = 0, totalSizeMB = 0.0, maxFiles = MAX_FILES };
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
                    _activeFiles.Clear();
                    
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

        /// <summary>
        /// Enforce the maximum file limit using LRU eviction
        /// </summary>
        private void EnforceFileLimit()
        {
            try
            {
                var files = Directory.GetFiles(_cacheDir, "*.pdf")
                    .Select(f => new FileInfo(f))
                    .OrderBy(f => f.LastAccessTime)
                    .ToList();

                // Remove oldest files that are not active
                while (files.Count >= MAX_FILES)
                {
                    var oldest = files.First();
                    if (!_activeFiles.Contains(oldest.Name))
                    {
                        try
                        {
                            oldest.Delete();
                            Console.WriteLine($"[HebrewBooksCacheManager] Evicted old file: {oldest.Name}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[HebrewBooksCacheManager] Error evicting file {oldest.Name}: {ex.Message}");
                        }
                    }
                    files.RemoveAt(0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooksCacheManager] Error enforcing file limit: {ex.Message}");
            }
        }

        /// <summary>
        /// Try to delete a file if it's not active
        /// </summary>
        private void TryDeleteFile(string fileName)
        {
            try
            {
                var filePath = Path.Combine(_cacheDir, fileName);
                if (File.Exists(filePath) && !_activeFiles.Contains(fileName))
                {
                    File.Delete(filePath);
                    Console.WriteLine($"[HebrewBooksCacheManager] Deleted inactive file: {fileName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HebrewBooksCacheManager] Error deleting file {fileName}: {ex.Message}");
            }
        }
    }
}