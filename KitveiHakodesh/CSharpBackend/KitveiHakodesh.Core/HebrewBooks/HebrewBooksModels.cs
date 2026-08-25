using System.Collections.Generic;

namespace KitveiHakodesh.Core.HebrewBooks
{
    /// <summary>
    /// One book as the bundled catalog lists it. The catalog is a listing of what exists
    /// upstream on hebrewbooks.org — having a row says nothing about whether the PDF is on
    /// this machine, which is what <see cref="HasLocalFile"/> answers and why it is stamped
    /// per search rather than stored.
    /// </summary>
    public sealed class HebrewBook
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public string PrintingPlace { get; set; } = "";
        public string PrintingYear { get; set; } = "";
        public int? Pages { get; set; }
        public string Categories { get; set; } = "";
        public bool HasLocalFile { get; set; }
    }

    /// <summary>Why an acquire did not produce a file. The caller turns this into a message —
    /// Core never phrases one (project rule 3).</summary>
    public enum HebrewBookAcquireFailure
    {
        /// <summary>Nothing failed; a path was produced.</summary>
        None = 0,

        /// <summary>The id was empty or not numeric, so it never reached the network.</summary>
        InvalidBookId,

        /// <summary>Upstream has no such book — it answered with a message page, not a PDF.</summary>
        NotFoundUpstream,

        /// <summary>Not on disk and downloading was not allowed. The caller decides whether to
        /// retry with downloading on.</summary>
        NotCachedAndDownloadDisallowed,

        /// <summary>The user cancelled this download. Distinct from an error: the caller closes
        /// the tab quietly rather than reporting a failure.</summary>
        Cancelled,

        /// <summary>The request never completed — offline, DNS, TLS, timeout.</summary>
        Network,

        /// <summary>Upstream answered, but not with success.</summary>
        HttpStatus,

        /// <summary>Anything else, with the detail in <see cref="HebrewBookAcquireResult.Detail"/>.</summary>
        Unexpected,
    }

    /// <summary>
    /// Outcome of an acquire. Exactly one of <see cref="Path"/> and <see cref="Failure"/> is
    /// meaningful: a non-null Path means success, otherwise Failure says why not.
    /// <see cref="Detail"/> is diagnostic text for a log — never for a user.
    /// </summary>
    public sealed class HebrewBookAcquireResult
    {
        public string? Path { get; }
        public HebrewBookAcquireFailure Failure { get; }
        public string? Detail { get; }

        private HebrewBookAcquireResult(string? path, HebrewBookAcquireFailure failure, string? detail)
        {
            Path = path;
            Failure = failure;
            Detail = detail;
        }

        public bool Succeeded => Path != null;

        public static HebrewBookAcquireResult Success(string path) =>
            new(path, HebrewBookAcquireFailure.None, null);

        public static HebrewBookAcquireResult Failed(HebrewBookAcquireFailure failure, string? detail = null) =>
            new(null, failure, detail);
    }

    /// <summary>What happened to a request to delete a downloaded PDF.</summary>
    public enum HebrewBookDeleteOutcome
    {
        Deleted = 0,

        /// <summary>The id was empty or not numeric.</summary>
        InvalidBookId,

        /// <summary>No local folder is configured, so there is nothing this could have deleted.</summary>
        NoLocalFolderConfigured,

        /// <summary>The folder is configured but holds no file for this id.</summary>
        NotThere,

        /// <summary>The file is there and could not be removed — in use, read-only, no permission.</summary>
        DeleteFailed,
    }

    /// <summary>Bytes received so far and the total when upstream sent a Content-Length
    /// (<see cref="Total"/> is 0 when it did not say).</summary>
    public readonly struct HebrewBookDownloadProgress
    {
        public long Received { get; }
        public long Total { get; }

        public HebrewBookDownloadProgress(long received, long total)
        {
            Received = received;
            Total = total;
        }
    }

    /// <summary>What a catalog update run did. Returned rather than logged so the orchestrator
    /// can surface it (project rule 3).</summary>
    public sealed class HebrewBooksCatalogUpdateResult
    {
        /// <summary>False when the run did not happen at all — see <see cref="SkipReason"/>.</summary>
        public bool Ran { get; set; }

        public HebrewBooksCatalogUpdateSkip SkipReason { get; set; }

        /// <summary>New rows written to the catalog.</summary>
        public int BooksAdded { get; set; }

        /// <summary>The highest id the walk probed, whether or not it held a book.</summary>
        public int LastIdChecked { get; set; }

        /// <summary>Ids that could not be fetched at all (network / HTTP errors), as opposed to
        /// ids upstream reports as empty. A run with many of these covered less ground than
        /// <see cref="LastIdChecked"/> suggests.</summary>
        public List<int> FetchFailures { get; } = new List<int>();

        /// <summary>Set when the run stopped on an error rather than finishing its walk. The
        /// rows written before it are still in the catalog — the walk resumes past them next time.</summary>
        public string? Error { get; set; }
    }

    /// <summary>Why an update run did nothing.</summary>
    public enum HebrewBooksCatalogUpdateSkip
    {
        /// <summary>It ran.</summary>
        None = 0,

        /// <summary>The interval since the last run has not elapsed.</summary>
        NotDueYet,

        /// <summary>No catalog file was found to update.</summary>
        CatalogMissing,

        /// <summary>The catalog file cannot be written — a read-only install or medium. The
        /// bundled catalog still serves searches; it just cannot grow here.</summary>
        CatalogNotWritable,
    }
}
