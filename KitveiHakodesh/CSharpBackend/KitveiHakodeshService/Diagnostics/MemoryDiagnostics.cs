using System.Diagnostics;
using System.Text;
using KitveiHakodeshService.Ipc;

namespace KitveiHakodeshService.Diagnostics;

/// <summary>
/// A plain-text memory report for <c>GET /khs-diag</c>: what the process holds right now and
/// why. Numbers only, no paths or user data, so it is safe without the bearer token (the
/// HTTP host is loopback-only). <c>GET /khs-diag/trim</c> runs the idle trim first and
/// reports before/after, which separates reclaimable memory from what truly stays resident.
/// </summary>
public static class MemoryDiagnostics
{
    public static string Report(bool ftsBusy, bool catalogBusy, IEnumerable<(string Name, string State)>? components = null)
    {
        var p = Process.GetCurrentProcess();
        var gc = GC.GetGCMemoryInfo(GCKind.Any);
        var sb = new StringBuilder();
        void Line(string name, double mb) => sb.Append(name.PadRight(30)).Append(mb.ToString("N1")).AppendLine(" MB");
        static double Mb(long b) => b / 1048576.0;

        sb.AppendLine("== process ==");
        Line("working set", Mb(p.WorkingSet64));
        Line("private (commit)", Mb(p.PrivateMemorySize64));
        Line("peak working set", Mb(p.PeakWorkingSet64));
        sb.AppendLine($"threads                       {p.Threads.Count}");
        sb.AppendLine($"handles                       {p.HandleCount}");

        sb.AppendLine("== managed heap ==");
        Line("GC heap size", Mb(gc.HeapSizeBytes));
        Line("GC committed", Mb(gc.TotalCommittedBytes));
        Line("GC fragmented", Mb(gc.FragmentedBytes));
        Line("GC.GetTotalMemory", Mb(GC.GetTotalMemory(false)));
        string[] gens = ["gen0", "gen1", "gen2", "LOH", "POH"];
        for (int i = 0; i < gc.GenerationInfo.Length && i < gens.Length; i++)
            Line("  " + gens[i] + " after last GC", Mb(gc.GenerationInfo[i].SizeAfterBytes));
        sb.AppendLine($"GC counts gen0/1/2            {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");
        // Commit the GC doesn't own: SQLite page caches, mapped views, native buffers, stacks.
        Line("native (private - GC commit)", Mb(p.PrivateMemorySize64 - gc.TotalCommittedBytes));

        if (components is not null)
        {
            sb.AppendLine("== resident components ==");
            foreach (var (name, state) in components) sb.AppendLine(name.PadRight(30) + state);
        }

        sb.AppendLine("== idle trimmer ==");
        sb.AppendLine($"fts busy                      {ftsBusy}");
        sb.AppendLine($"catalog TOC busy              {catalogBusy}");
        sb.AppendLine($"seconds since last RPC        {IdleMemoryTrimmer.SecondsSinceActivity:N0}");
        sb.AppendLine($"trims so far                  {IdleMemoryTrimmer.TrimCount}");
        if (IdleMemoryTrimmer.LastTrimUtc is { } t)
        {
            sb.AppendLine($"last trim                     {(DateTime.UtcNow - t).TotalSeconds:N0} s ago");
            Line("  working set before", IdleMemoryTrimmer.LastTrimBeforeMb);
            Line("  working set after", IdleMemoryTrimmer.LastTrimAfterMb);
        }
        return sb.ToString();
    }
}
