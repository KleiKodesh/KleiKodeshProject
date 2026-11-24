using System.Collections.Generic;
using MessagePack;

namespace KitveiHakodesh.Core.HebrewBooks
{
    /// <summary>
    /// One book as the bundled catalog lists it. The catalog is a listing of what exists
    /// upstream on hebrewbooks.org — having a row says nothing about whether the PDF is on
    /// this machine, which is what <see cref="HasLocalFile"/> answers and why it is stamped
    /// per search rather than stored.
    /// </summary>
    [MessagePackObject(keyAsPropertyName: true)]
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

    /// <summary>
    /// A HebrewBooks scan that Otzaria's map links to one of its books — a printed edition of
    /// the same work. <see cref="Id"/> is the HebrewBooks file id, so it opens like any other
    /// HebrewBooks book. <see cref="IsBest"/> marks the one edition Otzaria prefers;
    /// <see cref="IsCertain"/> is false for a match Otzaria rated only "likely".
    /// </summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class HebrewBooksEdition
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public string PrintingPlace { get; set; } = "";
        public string PrintingYear { get; set; } = "";
        public int? Pages { get; set; }
        public bool IsBest { get; set; }
        public bool IsCertain { get; set; }
        /// <summary>Already on disk: in the user's HebrewBooks folder or the app's hb-cache.
        /// Filled by the host per request, not by the map query.</summary>
        public bool IsOnDisk { get; set; }
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
    ///
    /// Settable properties rather than a private constructor and factories: this crosses the
    /// wire, and a type the deserializer cannot construct is not a wire type. The factories
    /// stay as the way CALLING code makes one, so the invariant still has a single home.
    /// </summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class HebrewBookAcquireResult
    {
        public string? Path { get; set; }
        public HebrewBookAcquireFailure Failure { get; set; }
        public string? Detail { get; set; }

        /// <summary>The upstream status for <see cref="HebrewBookAcquireFailure.HttpStatus"/>;
        /// <see cref="Detail"/> then carries the full response diagnosis.</summary>
        public int? HttpStatusCode { get; set; }

        [IgnoreMember]
        public bool Succeeded => Path != null;

        public static HebrewBookAcquireResult Success(string path) =>
            new HebrewBookAcquireResult { Path = path, Failure = HebrewBookAcquireFailure.None };

        public static HebrewBookAcquireResult Failed(HebrewBookAcquireFailure failure, string? detail = null) =>
            new HebrewBookAcquireResult { Failure = failure, Detail = detail };
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

        /// <summary>A Recycle Bin move was asked for but the file's drive has no bin, so it
        /// would have been deleted permanently. The file was left in place.</summary>
        NoRecycleBin,
    }

    /// <summary>Bytes received so far and the total when upstream sent a Content-Length
    /// (<see cref="Total"/> is 0 when it did not say).</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class HebrewBookDownloadProgress
    {
        public long Received { get; set; }
        public long Total { get; set; }
    }
}
