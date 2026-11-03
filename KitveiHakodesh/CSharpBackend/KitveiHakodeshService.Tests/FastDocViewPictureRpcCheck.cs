using System.Text.RegularExpressions;
using KitveiHakodesh.Core.FastDocView;
using KitveiHakodeshService.Ipc;
using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// The hosted app's picture leg, end to end below the WebView2 relay:
///   request bytes as the page encodes them (PascalCase keys, Args = nested MessagePack, the
///   relay forwards them untouched) -> Dispatcher op fastDocViewPicture -> Core
///   FastDocViewOpenDocuments.Picture -> library ReadPicture; reply envelope decoded as the page does.
///
///   --fastdocview-picture-rpc &lt;file with pictures&gt; [more files]
///
/// Checks per file: every picture id in the first blocks answers Ok with bytes whose magic matches the
/// content type; an unknown id, a closed document and a changed file answer Ok with an EMPTY result
/// (the page keeps the sized empty box and logs nothing).
/// </summary>
public static class FastDocViewPictureRpcCheck
{
    private static readonly Regex PictureId = new("data-word-picture=\"([^\"]*)\"", RegexOptions.CultureInvariant);

    public static int Run(IReadOnlyList<string> files)
    {
        int failures = 0;
        void Expect(bool ok, string what)
        {
            if (!ok) failures++;
            Console.WriteLine((ok ? "  ok:   " : "  FAIL: ") + what);
        }

        foreach (string source in files)
        {
            Console.WriteLine($"[picture-rpc] {Path.GetFileName(source)}");
            string work = Path.Combine(Path.GetTempPath(), "khs-picture-rpc-" + Guid.NewGuid().ToString("N") + Path.GetExtension(source));
            File.Copy(source, work);
            var documents = new FastDocViewOpenDocuments();
            var dispatcher = new Dispatcher(null!, null!, null!, null!, null!, null!, null!, null!, null!, documents,
                null!, null!, null!, null!, null!, NullLogger<Dispatcher>.Instance);
            try
            {
                FastDocViewSummary summary = documents.Open(work);
                string id = summary.DocumentId;
                while (!documents.Scan(id, FastDocViewOpenDocuments.MaxBlocksPerScan).IsIndexComplete) { }
                int blockCount = documents.Open(work).BlockCount;
                var pictureIds = new List<string>();
                for (int first = 0; first < blockCount && pictureIds.Count < 50; first += FastDocViewOpenDocuments.MaxBlocksPerRequest)
                    foreach (FastDocViewBlock block in documents.Blocks(id, first, FastDocViewOpenDocuments.MaxBlocksPerRequest))
                        foreach (Match match in PictureId.Matches(block.Html))
                        {
                            pictureIds.Add(System.Net.WebUtility.HtmlDecode(match.Groups[1].Value));
                            int tagStart = block.Html.LastIndexOf("<img", match.Index, StringComparison.Ordinal);
                            int tagEnd = block.Html.IndexOf('>', match.Index);
                            if (tagStart >= 0 && tagEnd > tagStart) Console.WriteLine($"         block {block.Index}: {block.Html[tagStart..(tagEnd + 1)]}");
                        }
                Expect(pictureIds.Count > 0, $"{blockCount} blocks, {pictureIds.Count} picture id(s) in the blocks");

                foreach (string pictureId in pictureIds)
                {
                    var reply = Call(dispatcher, id, pictureId);
                    bool ok = reply.Ok && reply.ContentType is not null && reply.Bytes is { Length: > 0 } && MagicMatches(reply.ContentType, reply.Bytes);
                    Expect(ok, $"picture {pictureId}: {reply.ContentType} {reply.Bytes?.Length ?? 0} bytes{(reply.Error is null ? "" : " error " + reply.Error)}");
                }

                var missing = Call(dispatcher, id, "no-such-picture");
                Expect(missing.Ok && missing.Empty, "unknown picture id: Ok, empty result");
                var garbage = Call(dispatcher, id, "../../etc/passwd");
                Expect(garbage.Ok && garbage.Empty, "path-like picture id: Ok, empty result");

                if (pictureIds.Count > 0)
                {
                    File.SetLastWriteTimeUtc(work, DateTime.UtcNow.AddMinutes(1));
                    File.AppendAllText(work, " ");
                    var changed = Call(dispatcher, id, pictureIds[0]);
                    Expect(changed.Ok && changed.Empty, "file changed on disk: Ok, empty result");
                    documents.Close(id);
                    var closed = Call(dispatcher, id, pictureIds[0]);
                    Expect(closed.Ok && closed.Empty, "document closed: Ok, empty result");
                }
            }
            catch (Exception exception)
            {
                Expect(false, "threw: " + exception);
            }
            finally
            {
                documents.Dispose();
                try { File.Delete(work); } catch (IOException) { }
            }
        }
        Console.WriteLine(failures == 0 ? "[picture-rpc] ALL PASSED" : $"[picture-rpc] {failures} FAILED");
        return failures == 0 ? 0 : 1;
    }

    private sealed record Reply(bool Ok, bool Empty, string? Error, string? ContentType, byte[]? Bytes);

    /// <summary>Encodes like serviceClient.encodeRequest, decodes like serviceCallCore.</summary>
    private static Reply Call(Dispatcher dispatcher, string documentId, string pictureId)
    {
        byte[] args = MessagePackSerializer.Serialize(new Dictionary<string, object> { ["DocumentId"] = documentId, ["PictureId"] = pictureId });
        byte[] request = MessagePackSerializer.Serialize(new Dictionary<string, object> { ["Op"] = "fastDocViewPicture", ["Args"] = args });
        byte[] response = dispatcher.DispatchAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        var envelope = MessagePackSerializer.Deserialize<Dictionary<string, object?>>(response);
        bool ok = envelope.TryGetValue("Ok", out object? okValue) && okValue is true;
        string? error = envelope.TryGetValue("Error", out object? e) ? e as string : null;
        byte[]? result = envelope.TryGetValue("Result", out object? r) ? r as byte[] : null;
        if (result is null || result.Length == 0) return new Reply(ok, true, error, null, null);
        var picture = MessagePackSerializer.Deserialize<Dictionary<string, object?>>(result);
        return new Reply(ok, false, error, picture.GetValueOrDefault("ContentType") as string, picture.GetValueOrDefault("Bytes") as byte[]);
    }

    private static bool MagicMatches(string contentType, byte[] bytes)
    {
        bool Starts(params byte[] magic) => bytes.Length >= magic.Length && bytes.AsSpan(0, magic.Length).SequenceEqual(magic);
        return contentType switch
        {
            "image/png" => Starts(0x89, 0x50, 0x4E, 0x47),
            "image/jpeg" => Starts(0xFF, 0xD8, 0xFF),
            "image/gif" => Starts(0x47, 0x49, 0x46),
            "image/bmp" => Starts(0x42, 0x4D),
            "image/svg+xml" => System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 512)).Contains("<svg", StringComparison.Ordinal),
            _ => true,
        };
    }
}
