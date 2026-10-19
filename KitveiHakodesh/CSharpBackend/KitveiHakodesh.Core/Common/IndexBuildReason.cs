using System;
using MessagePack;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>
    /// Why an index last started building (a stale stamp in words, a reset, no index yet) and
    /// when. Each index coordinator records one per build, logs it, and reports it in its status
    /// so a host can show it (the frontend writes it to its console).
    /// </summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class IndexBuildReason
    {
        public string Reason { get; set; } = "";
        public DateTime AtUtc { get; set; }

        public static IndexBuildReason Now(string reason) => new() { Reason = reason, AtUtc = DateTime.UtcNow };
    }

    /// <summary>The three indexes' last build reasons (indexBuildReasons wire shape); each null
    /// until that index first builds in this process.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class IndexBuildReasons
    {
        public IndexBuildReason? FullTextSearch { get; set; }
        public IndexBuildReason? CatalogToc { get; set; }
        public IndexBuildReason? LocalFiles { get; set; }
    }
}
