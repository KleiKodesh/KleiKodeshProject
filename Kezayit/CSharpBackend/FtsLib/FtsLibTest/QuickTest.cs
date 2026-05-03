namespace FtsLibTest
{
    /// <summary>
    /// Fast development test — indexes the first 500k lines only.
    /// Use this for quick iteration; use FullDbTest.Run() for the complete index.
    /// </summary>
    internal static class QuickTest
    {
        public static void Run() => FullDbTest.Run(lineLimit: 500_000);
    }
}
