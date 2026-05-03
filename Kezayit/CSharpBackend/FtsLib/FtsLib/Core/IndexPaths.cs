using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FtsLib.Core
{
    public class IndexPaths
    {
        protected readonly string IndexPath;
        protected string PostingsPath => Path.Combine(IndexPath, "postings.dat");
        protected string MetaDbPath => Path.Combine(IndexPath, "Meta.db");

        public IndexPaths(string indexPath) 
        {
            IndexPath  = !string.IsNullOrEmpty(indexPath) ? indexPath :
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fts-index"); 
            if (!Directory.Exists(IndexPath)) 
                Directory.CreateDirectory(IndexPath);
        }
    }
}
