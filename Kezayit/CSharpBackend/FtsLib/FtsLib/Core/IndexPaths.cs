using System;
using System.IO;

namespace FtsLib.Core
{
    internal class IndexPaths
    {
        protected readonly string IndexPath;

        /// <summary>
        /// Sorted varint-delta file that records logically deleted doc IDs.
        /// Absent when no deletions have been made.
        /// </summary>
        protected string DeletesFile => Path.Combine(IndexPath, "deletes.bin");

        public IndexPaths(string indexPath)
        {
            IndexPath = !string.IsNullOrEmpty(indexPath) ? indexPath :
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fts-index");
            if (!Directory.Exists(IndexPath))
                Directory.CreateDirectory(IndexPath);
        }
    }
}
