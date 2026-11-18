using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using KitveiHakodesh.Core.Common;

namespace KitveiHakodesh.Core.WebsiteIcons;

/// <summary>
/// The Websites page's icons, kept on disk beside the app (folder "website-icons") so they are
/// not fetched from every site on every render. An icon is refetched when its copy is older than
/// <see cref="RefetchAfter"/>; if that refetch fails the old copy is still served.
///
/// Only image responses (Content-Type image/*) up to <see cref="MaxIconBytes"/> are accepted.
/// Each icon is two files named by the SHA-256 of its address: "&lt;hash&gt;" (the bytes) and
/// "&lt;hash&gt;.content-type" (one line).
///
/// The service endpoint takes any address, so the downloader only connects to PUBLIC internet
/// addresses (checked at connect time, which covers redirects and DNS tricks): a caller cannot
/// use it to reach localhost or the LAN. The folder keeps at most <see cref="MaxIcons"/> icons;
/// the oldest go first.
/// </summary>
public sealed class WebsiteIconCache
{
    public static readonly TimeSpan RefetchAfter = TimeSpan.FromDays(7);
    public const int MaxIconBytes = 2 * 1024 * 1024;
    public const int MaxIcons = 200;

    private static readonly HttpClient Http = CreateBrowserLikeClient();

    private readonly string _folder;

    public WebsiteIconCache(string? folder = null) =>
        _folder = folder ?? AppFileLocator.ResolveWritablePath("website-icons");

    /// <summary>The icon at <paramref name="url"/>: fresh from disk, else downloaded (and kept),
    /// else the stale copy. Null for a non-http(s) address or when nothing could be had.</summary>
    public async Task<WebsiteIcon?> GetAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return null;

        string bytesPath = Path.Combine(_folder, HashOf(uri.AbsoluteUri));
        string typePath = bytesPath + ".content-type";
        WebsiteIcon? cached = ReadCached(bytesPath, typePath);
        if (cached is not null && DateTime.UtcNow - File.GetLastWriteTimeUtc(bytesPath) < RefetchAfter)
            return cached;

        WebsiteIcon? downloaded = await TryDownloadAsync(uri, ct);
        if (downloaded is null) return cached;
        TryWrite(bytesPath, typePath, downloaded);
        return downloaded;
    }

    private static WebsiteIcon? ReadCached(string bytesPath, string typePath)
    {
        try
        {
            if (!File.Exists(bytesPath) || !File.Exists(typePath)) return null;
            return new WebsiteIcon { ContentType = File.ReadAllText(typePath).Trim(), Bytes = File.ReadAllBytes(bytesPath) };
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static async Task<WebsiteIcon?> TryDownloadAsync(Uri uri, CancellationToken ct)
    {
        try
        {
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;
            string? contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is null || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
            if (response.Content.Headers.ContentLength > MaxIconBytes) return null;

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[16 * 1024];
            int read;
            while ((read = await body.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > MaxIconBytes) return null;
                buffer.Write(chunk, 0, read);
            }
            if (buffer.Length == 0) return null;
            return new WebsiteIcon { ContentType = contentType, Bytes = buffer.ToArray() };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; } // HttpClient timeout
        catch (HttpRequestException) { return null; }
        catch (IOException) { return null; }
    }

    /// <summary>Writes through temp files and a rename, so a reader never sees half an icon.
    /// A failed write only means the next request downloads again.</summary>
    private void TryWrite(string bytesPath, string typePath, WebsiteIcon icon)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            string suffix = "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(typePath + suffix, icon.ContentType);
            File.WriteAllBytes(bytesPath + suffix, icon.Bytes);
            File.Move(typePath + suffix, typePath, overwrite: true);
            File.Move(bytesPath + suffix, bytesPath, overwrite: true);
            TrimToMaxIcons();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Deletes the oldest icons beyond <see cref="MaxIcons"/>.</summary>
    private void TrimToMaxIcons()
    {
        var icons = new DirectoryInfo(_folder).GetFiles()
            .Where(f => !f.Name.Contains('.'))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(MaxIcons);
        foreach (FileInfo old in icons)
        {
            try
            {
                old.Delete();
                File.Delete(old.FullName + ".content-type");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string HashOf(string url) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();

    /// <summary>Some sites refuse requests that do not look like a browser.</summary>
    private static HttpClient CreateBrowserLikeClient()
    {
        var handler = new SocketsHttpHandler { ConnectCallback = ConnectToPublicAddressOnlyAsync, MaxAutomaticRedirections = 3 };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        return client;
    }

    /// <summary>Connects only to a public internet address; loopback, private, link-local and
    /// other non-public ranges are refused.</summary>
    private static async ValueTask<Stream> ConnectToPublicAddressOnlyAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        IPAddress? target = addresses.FirstOrDefault(IsPublic)
            ?? throw new HttpRequestException("website icon: not a public internet address");
        var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return false;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Multicast);
        byte[] b = address.GetAddressBytes();
        return !(b[0] == 10 || b[0] == 0 || b[0] >= 224
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127));
    }
}
