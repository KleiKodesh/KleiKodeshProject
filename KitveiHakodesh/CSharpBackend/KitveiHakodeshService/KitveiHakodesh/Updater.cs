using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using KitveiHakodesh.Core.Settings;
using KitveiHakodeshService.Ipc;

namespace KitveiHakodeshService.KitveiHakodesh;

/// <summary>
/// One update pass: is a newer release on GitHub, and if so, its installer on disk -
/// downloaded now, or reused when that exact file is already here. Launching it and the
/// weekly rhythm are <see cref="UpdaterService"/>'s.
///
/// The rules are the ones UpdateCheckerLib applied inside the hosts until the service took
/// this over: the installed version is the installer's own stamp (HKCU\SOFTWARE\KleiKodesh
/// Version - read through <see cref="UserProfile"/>, because this runs as SYSTEM serving the
/// logged-on user), the single AnyCPU installer asset, and the
/// "longer version string wins" comparison.
/// </summary>
public sealed class Updater(HttpClient http, ILogger<Updater> logger)
{
    /// <summary>One pass at a time: the weekly pass and a "check now" from the settings page
    /// must not download the same installer side by side.</summary>
    private readonly SemaphoreSlim passGate = new(1, 1);

    /// <summary>Set when an app registers with the service; <see cref="UpdaterService"/> then
    /// checks at once, besides its weekly pass. At most one request is pending.</summary>
    internal SemaphoreSlim CheckRequested { get; } = new(0, 1);

    public void RequestCheck()
    {
        try { CheckRequested.Release(); }
        catch (SemaphoreFullException) { /* one is already pending */ }
    }

    /// <summary>A check/download pass is running; the service must not stop under it
    /// (Http/OwnerWatcher).</summary>
    public bool IsBusy => passGate.CurrentCount == 0;

    // ── Download progress, pushed to the apps (updateProgressStream) ──────────────────────

    /// <summary>The release being downloaded now ("" when none) and its bytes so far.</summary>
    private string downloadingVersion = "";
    private long downloadReceived;
    private long downloadTotal;

    /// <summary>One wake-up channel per open stream; a change drops into each (at most one
    /// pending, so a slow client only ever skips to the latest snapshot).</summary>
    private readonly List<System.Threading.Channels.Channel<bool>> progressSubs = [];

    private UpdateProgressFrame ProgressSnapshot()
    {
        var ready = ReadyInstaller();
        return new UpdateProgressFrame
        {
            DownloadingVersion = Volatile.Read(ref downloadingVersion),
            Received = Interlocked.Read(ref downloadReceived),
            Total = Interlocked.Read(ref downloadTotal),
            ReadyVersion = ready?.Version ?? "",
        };
    }

    private void NotifyProgress()
    {
        lock (progressSubs)
            foreach (var ch in progressSubs) ch.Writer.TryWrite(true);
    }

    /// <summary>
    /// Pushes a snapshot now and on every change - download start, progress, finish (the update
    /// is then ready) - until the client leaves. Never ends on its own: an app keeps one open
    /// for its whole session, so a download the service starts later reaches it at once.
    /// </summary>
    public async Task StreamProgressAsync(Func<UpdateProgressFrame, Task> emit, CancellationToken ct)
    {
        var ch = System.Threading.Channels.Channel.CreateBounded<bool>(
            new System.Threading.Channels.BoundedChannelOptions(1)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite,
            });
        lock (progressSubs) progressSubs.Add(ch);
        try
        {
            await emit(ProgressSnapshot());
            while (true)
            {
                await ch.Reader.ReadAsync(ct);
                await emit(ProgressSnapshot());
            }
        }
        catch (OperationCanceledException) { /* client left */ }
        catch (IOException) { /* client disconnected mid-stream */ }
        catch (ObjectDisposedException) { /* connection torn down under us */ }
        finally
        {
            lock (progressSubs) progressSubs.Remove(ch);
        }
    }

    private const string RegistryKeyPath = @"SOFTWARE\KleiKodesh";
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest";

    /// <summary>Where installers are downloaded: beside the service, which the installer
    /// leaves alone on update.</summary>
    public static string DownloadFolder => Path.Combine(AppContext.BaseDirectory, "updates");

    /// <summary>
    /// The downloaded installer of the newest release that is newer than the installed version,
    /// or null. Read from disk every time (downloads land complete - a partial one stays
    /// ".part"), so it survives service restarts and needs no state. Null while automatic
    /// updates are turned off, so a user who turned them off is not asked.
    /// </summary>
    public static (string Path, string Version)? ReadyInstaller()
    {
        if (AppSettingsRegistry.GetTurnOffUpdates()) return null;
        string? installed = InstalledVersion();
        if (string.IsNullOrEmpty(installed) || !Directory.Exists(DownloadFolder)) return null;

        (string Path, string Version)? best = null;
        foreach (string file in Directory.EnumerateFiles(DownloadFolder, "KleiKodeshSetup-*.exe"))
        {
            string tag = Path.GetFileNameWithoutExtension(file).Substring("KleiKodeshSetup-".Length);
            int variantDash = tag.IndexOf('-');
            if (variantDash > 0) tag = tag.Substring(0, variantDash);
            if (CompareVersions(tag, installed) <= 0) continue;
            if (best is null || CompareVersions(tag, best.Value.Version) > 0) best = (file, tag);
        }
        return best;
    }

    /// <summary>The installer's own stamp; null when this is not an installed copy.</summary>
    public static string? InstalledVersion()
    {
        try
        {
            using var key = UserProfile.Hive.OpenSubKey(RegistryKeyPath);
            return key?.GetValue("Version")?.ToString();
        }
        catch { return null; }
    }

    /// <summary>
    /// The installer of a release newer than the installed one, on disk and complete; null
    /// when there is nothing newer (or no usable asset). Throws when GitHub or the download
    /// fails - the caller logs it and tries again next week.
    /// </summary>
    public async Task<string?> FindOrDownloadNewerInstallerAsync(CancellationToken ct)
    {
        await passGate.WaitAsync(ct);
        try { return await FindOrDownloadNewerInstallerUnguardedAsync(ct); }
        finally
        {
            Volatile.Write(ref downloadingVersion, "");
            Interlocked.Exchange(ref downloadReceived, 0);
            Interlocked.Exchange(ref downloadTotal, 0);
            passGate.Release();
            // Download over (or reused / nothing newer / failed): the apps see the ready
            // update, or the bar disappear, right now.
            NotifyProgress();
        }
    }

    private async Task<string?> FindOrDownloadNewerInstallerUnguardedAsync(CancellationToken ct)
    {
        string? installed = InstalledVersion();
        if (string.IsNullOrEmpty(installed)) return null;
        DeleteInstallersNotNewerThan(installed);

        GithubRelease release = await LatestReleaseAsync(ct);
        if (string.IsNullOrEmpty(release.TagName) || CompareVersions(release.TagName, installed) <= 0)
        {
            logger.LogInformation("Update check: {Latest} is not newer than installed {Installed}", release.TagName, installed);
            return null;
        }

        GithubReleaseAsset? asset = ResolveInstallerAsset(release);
        if (asset is null)
        {
            logger.LogWarning("Update {Version}: no installer asset for this variant", release.TagName);
            return null;
        }

        Directory.CreateDirectory(DownloadFolder);
        string target = Path.Combine(DownloadFolder, asset.Name);
        DeleteOtherInstallers(keep: target);

        // Already downloaded on an earlier pass (e.g. the user closed the installer last week):
        // the same file name at the full published size is that release, so reuse it.
        if (File.Exists(target) && new FileInfo(target).Length == asset.Size)
        {
            logger.LogInformation("Update {Version}: installer already downloaded", release.TagName);
            return target;
        }

        logger.LogInformation("Update {Version}: downloading {Asset} ({Size} bytes)", release.TagName, asset.Name, asset.Size);
        string partial = target + ".part";
        // The client's timeout only covers the headers once the body is streamed; a stalled
        // connection must not hang the pass until the service stops.
        using var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        downloadTimeout.CancelAfter(TimeSpan.FromMinutes(60));
        ct = downloadTimeout.Token;
        try
        {
            using (var response = await http.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var destination = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None);
                await CopyWithProgressAsync(source, destination, release.TagName, asset.Size, ct);
            }

            long length = new FileInfo(partial).Length;
            if (length != asset.Size)
                throw new IOException($"download incomplete: {length} of {asset.Size} bytes");

            File.Move(partial, target, overwrite: true);
            return target;
        }
        finally
        {
            try { if (File.Exists(partial)) File.Delete(partial); } catch { /* next pass retries */ }
        }
    }

    /// <summary>The download's copy loop, publishing bytes received. Pushes at most every
    /// 250 ms - a frame per buffer would flood the apps.</summary>
    private async Task CopyWithProgressAsync(Stream source, Stream destination, string version, long total, CancellationToken ct)
    {
        Interlocked.Exchange(ref downloadTotal, total);
        Interlocked.Exchange(ref downloadReceived, 0);
        Volatile.Write(ref downloadingVersion, version);
        NotifyProgress();

        byte[] buffer = new byte[81920];
        long received = 0;
        long lastNotify = Environment.TickCount64;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;
            Interlocked.Exchange(ref downloadReceived, received);
            long now = Environment.TickCount64;
            if (now - lastNotify >= 250)
            {
                lastNotify = now;
                NotifyProgress();
            }
        }
    }

    private async Task<GithubRelease> LatestReleaseAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        // GitHub rejects requests with no User-Agent; the shared client already sends one.
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new HttpRequestException("no published release (404)");
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync(body, UpdaterJsonContext.Default.GithubRelease, ct)
            ?? throw new InvalidOperationException("the release response was empty");
    }

    private static GithubReleaseAsset? ResolveInstallerAsset(GithubRelease release)
    {
        if (release.Assets is null || string.IsNullOrEmpty(release.TagName)) return null;
        // One installer since 2026-10-11: AnyCPU, no platform suffix.
        return FindAsset(release, "KleiKodeshSetup-" + release.TagName + ".exe");
    }

    private static GithubReleaseAsset? FindAsset(GithubRelease release, string name) =>
        release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)
            && a.Size > 0 && !string.IsNullOrEmpty(a.BrowserDownloadUrl));

    /// <summary>Installers of the version already installed (or older) are done with:
    /// about 100 MB each, they would otherwise sit here until the next release.</summary>
    private static void DeleteInstallersNotNewerThan(string installed)
    {
        if (!Directory.Exists(DownloadFolder)) return;
        foreach (string file in Directory.EnumerateFiles(DownloadFolder, "KleiKodeshSetup-*.exe"))
        {
            // "KleiKodeshSetup-v8.7.3.exe" -> "v8.7.3" (an older "-x64" / "-x86" suffix is cut too)
            string tag = Path.GetFileNameWithoutExtension(file).Substring("KleiKodeshSetup-".Length);
            int variantDash = tag.IndexOf('-');
            if (variantDash > 0) tag = tag.Substring(0, variantDash);
            if (CompareVersions(tag, installed) > 0) continue;
            try { File.Delete(file); } catch { /* locked by a running installer - next pass */ }
        }
    }

    /// <summary>Old downloads are dead weight once a newer release exists.</summary>
    private static void DeleteOtherInstallers(string keep)
    {
        foreach (string file in Directory.EnumerateFiles(DownloadFolder, "KleiKodeshSetup-*.exe"))
        {
            if (string.Equals(file, keep, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(file); } catch { /* locked by a running installer - next pass */ }
        }
    }

    /// <summary>
    /// Orders two version strings. A longer string wins over a shorter one ("1.2.3.1" over
    /// "1.2.3") before components are compared - the rule the release tags were built around.
    /// </summary>
    public static int CompareVersions(string? candidate, string? installed)
    {
        string left = candidate?.TrimStart('v') ?? "";
        string right = installed?.TrimStart('v') ?? "";
        if (Version.TryParse(left, out Version? leftVersion) && Version.TryParse(right, out Version? rightVersion))
        {
            int byComponentCount = ComponentCount(leftVersion).CompareTo(ComponentCount(rightVersion));
            return byComponentCount != 0 ? byComponentCount : leftVersion.CompareTo(rightVersion);
        }
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static int ComponentCount(Version version) =>
        version.Revision >= 0 ? 4 : version.Build >= 0 ? 3 : 2;
}

/// <summary>A release as the GitHub API reports it (somebody else's JSON).</summary>
internal sealed class GithubRelease
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("assets")] public List<GithubReleaseAsset> Assets { get; set; } = [];
}

internal sealed class GithubReleaseAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
}

/// <summary>Source-generated: the service publishes native AOT.</summary>
[JsonSerializable(typeof(GithubRelease))]
internal sealed partial class UpdaterJsonContext : JsonSerializerContext;
