namespace FtsEngine.Core
{
    /// <summary>
    /// A single (term, docId) occurrence — the atomic unit buffered before flushing to a run.
    /// Comparable by term first, then docId, so sorting produces the correct merge order.
    /// </summary>
    internal struct TermOccurence
    {
        public string Term;
        public int    DocId;

        public TermOccurence(string term, int docId)
        {
            Term  = term;
            DocId = docId;
        }
    }
}
