using global::FastDocView;

namespace KitveiHakodesh.Core.FastDocView;

/// <summary>
/// A file the library could not open, with why. The message is "<see cref="Marker"/>:&lt;code&gt;: detail" - the page
/// reads the code (encrypted, corrupt, online-only, not-found, unsupported, access-denied) and shows its own
/// translated text, with the detail as a secondary line. Unclassified failures keep their plain message.
/// </summary>
public sealed class FastDocViewOpenFailedException(string code, string detail, Exception? cause = null)
    : Exception(MessageFor(code, detail), cause)
{
    public const string Marker = "FastDocViewOpenFailed";
    public const string Encrypted = "encrypted", Corrupt = "corrupt", OnlineOnly = "online-only",
                        NotFound = "not-found", Unsupported = "unsupported", AccessDenied = "access-denied";

    public string Code { get; } = code;

    public static string MessageFor(string code, string detail) => $"{Marker}:{code}: {detail}";

    /// <summary>The code for a library open failure, or null when it is not one a reader can act on.</summary>
    public static string? CodeOf(Exception exception) => WordDocumentOpenFailures.Classify(exception) switch
    {
        WordDocumentOpenFailure.Encrypted => Encrypted,
        WordDocumentOpenFailure.Corrupt => Corrupt,
        WordDocumentOpenFailure.OnlineOnly => OnlineOnly,
        WordDocumentOpenFailure.NotFound => NotFound,
        WordDocumentOpenFailure.UnsupportedFormat => Unsupported,
        WordDocumentOpenFailure.AccessDenied => AccessDenied,
        _ => null,
    };
}
