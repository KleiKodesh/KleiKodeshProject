using MessagePack;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>Where a library download stands. The phases run in this order; a run ends in
    /// <see cref="Completed"/> or <see cref="Failed"/>.</summary>
    public enum SeforimLibraryDownloadPhase
    {
        /// <summary>Nothing has run since the process started.</summary>
        Idle = 0,

        /// <summary>Asking GitHub which release holds the newest library.</summary>
        FindingRelease,

        /// <summary>Fetching the compressed database. Resumes from the bytes already on disk.</summary>
        Downloading,

        /// <summary>Checking the downloaded file against the release's sha256.</summary>
        Verifying,

        /// <summary>Unpacking the zstd file into seforim.db.</summary>
        Extracting,

        /// <summary>Applying an update patch to the library this app downloaded earlier.</summary>
        Patching,

        /// <summary>seforim.db is in place.</summary>
        Completed,

        /// <summary>The run stopped; <see cref="SeforimLibraryDownloadProgress.Failure"/> says why.</summary>
        Failed,
    }

    /// <summary>Why a run failed. The host words the message — Core never phrases one.</summary>
    public enum SeforimLibraryDownloadFailure
    {
        None = 0,

        /// <summary>GitHub or the file host could not be reached. The bytes already fetched are kept.</summary>
        Network,

        /// <summary>No stable release carries a library.</summary>
        NoCompatibleRelease,

        /// <summary>The drive holding the library folder has too little free space.</summary>
        NotEnoughDiskSpace,

        /// <summary>The downloaded file did not match the release's sha256. It was deleted.</summary>
        ChecksumMismatch,

        /// <summary>The file could not be unpacked, or the result is not a seforim database.</summary>
        CorruptArchive,

        /// <summary>Reading or writing the library folder failed.</summary>
        DiskError,

        /// <summary>The user cancelled. The bytes already fetched are kept for a later resume.</summary>
        Cancelled,

        /// <summary>Anything else; <see cref="SeforimLibraryDownloadProgress.Detail"/> holds the exception.</summary>
        Unexpected,

        /// <summary>Import: a part of the picked library is missing or not fully downloaded.</summary>
        ImportIncomplete,

        /// <summary>Import: the picked file is not a library archive.</summary>
        ImportNotALibrary,
    }

    /// <summary>A snapshot of the current run, pushed to the apps while it runs.
    /// <see cref="Received"/> / <see cref="Total"/> count bytes within the CURRENT phase.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class SeforimLibraryDownloadProgress
    {
        public SeforimLibraryDownloadPhase Phase { get; set; }
        public long Received { get; set; }
        public long Total { get; set; }

        /// <summary>The release tag being fetched, e.g. "v32-20261010190601". Empty before it is known.</summary>
        public string ReleaseTag { get; set; } = "";

        public SeforimLibraryDownloadFailure Failure { get; set; }

        /// <summary>Exception text and response facts, for the console only.</summary>
        public string? Detail { get; set; }

        /// <summary>True from the user's request until the database is in place — survives restarts.</summary>
        public bool Pending { get; set; }

        /// <summary>True while a run is executing in this process.</summary>
        public bool Running { get; set; }
    }
}
