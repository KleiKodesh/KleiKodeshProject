namespace MinimalIndexer
{
    /// <summary>
    /// Common interface for approximate membership filters (BloomFilter, BinaryFuseFilter).
    /// Allows the indexer, writer, and reader to work with either implementation
    /// without null checks or type switches.
    /// </summary>
    public interface IApproximateFilter
    {
        /// <summary>Format identifier stored in the .dat file header (bitCount field).</summary>
        int Size { get; }

        /// <summary>Hash function count stored in the .dat file header.</summary>
        int HashFunctions { get; }

        bool Contains(string item);
        bool ContainsAll(string[] items);

        byte[] GetBytes();
        int GetByteSize();
    }
}
