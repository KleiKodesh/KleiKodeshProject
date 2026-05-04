using System;
using System.IO;

namespace FtsLib.Core
{
    public class IndexPaths
    {
        protected readonly string IndexPath;

        /// <summary>
        /// Directory that holds all segment files (seg_L_ID.dat + seg_L_ID.db).
        /// This is the only persistent storage — there is no separate postings.dat or Meta.db.
        /// </summary>
        protected string SegmentsDir => Path.Combine(IndexPath, "segments");

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
