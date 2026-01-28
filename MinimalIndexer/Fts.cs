using System;

namespace MinimalIndexer
{
    public sealed class Fts
    {
        readonly SearchEngine _engine = new SearchEngine();


        // Optional manual chunk sizes
        public void SetChunkSize(short chunkI)
        {
            //if (chunkI <= 0)
            //    throw new ArgumentException("Chunk must be positive.");

            //_chunkI = chunkI;
            //_chunkII = Math.Max((short)(chunkI / 10), (short)1);
        }

        // Create index
        public void CreateIndex(bool silent = false)
        {
            //if (_chunkI.HasValue && _chunkII.HasValue)
            _engine.CreateIndex();
            //else
            //_engine.CreateIndexWithAutoChunkSize(silentMode: silent);
        }

        // Search
        public void Search(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Search text is empty.");

            _engine.SearchIndex(text);
        }

        // Utilities
        public void CalculateOptimalChunkSizes()
        {
            //_engine.CalculateOptimalChunkSizes();
        }

        public void Diagnose()
        {
            //IndexDiagnostic.DiagnoseIndex();
        }
    }
}
