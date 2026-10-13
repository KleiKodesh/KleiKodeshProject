#nullable enable
using System;
using System.IO;
using KitveiHakodesh.Core.Settings;

namespace KitveiHakodesh.Core.HebrewBooks;

/// <summary>Where downloaded HebrewBooks PDFs are saved: the folder a caller asked for, else
/// the one saved in the shared registry value (the same one every host reads); and which
/// {bookId}.pdf in it is a book.</summary>
public static class HebrewBooksLocalFolder
{
    public static string Resolve(string? requestedFolder) =>
        string.IsNullOrWhiteSpace(requestedFolder)
            ? AppSettingsRegistry.GetHebrewBooksLocalFolder()
            : requestedFolder!;

    /// <summary>
    /// Book ids are numeric upstream. Anything else could steer the request URL or escape
    /// the folder the {id}.pdf name is built in, so it never gets that far. EVERY entry
    /// point that turns a caller's id into a path or a URL checks this first.
    ///
    /// Here, not in HebrewBooksDownloader, because the WebView2 host links this file to
    /// check ids itself (reveal in Explorer) - one copy of "what is a book id".
    /// </summary>
    public static bool IsValidBookId(string? bookId)
    {
        if (string.IsNullOrWhiteSpace(bookId)) return false;
        foreach (char c in bookId!)
            if (c < '0' || c > '9') return false;
        return true;
    }

    /// <summary>
    /// The full path to {bookId}.pdf in the user's folder when it is there and reachable,
    /// else null. Swallows I/O errors — a disconnected drive, a malformed path — so the
    /// caller falls through to downloading instead of failing.
    /// </summary>
    public static string? LocalFolderHit(string? localFolder, string bookId)
    {
        if (string.IsNullOrWhiteSpace(localFolder)) return null;
        try
        {
            string candidate = Path.Combine(localFolder!, bookId + ".pdf");
            return File.Exists(candidate) ? candidate : null;
        }
        catch (Exception)
        {
            return null; // drive disconnected or the path is malformed — fall through to download
        }
    }
}
