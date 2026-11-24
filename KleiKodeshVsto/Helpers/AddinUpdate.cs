// ALL of the Word add-in's update logic lives in this one file. The add-in updates itself,
// without the KitveiHakodesh service (which is not installed when Kitvei Hakodesh is not): one
// background check when a fresh side pane opens (at most weekly), what is ready read straight
// from disk and the registry, and shows an "Update available" ribbon button
// (plus the settings view's "Updates" section). Other files only make one-line calls into here:
//   TaskpaneManager.OnPaneCreated   -> AddinUpdateState.PaneOpened()
//   KeliKodeshRibbon                -> AddinUpdateRibbon
//   AddinCommands "Update"          -> AddinUpdateInstall.PromptAndInstall()
//   RibbonSettingsView              -> AddinUpdateSettingsSection
using KitveiHakodesh.Core.Settings;
using KitveiHakodesh.Core.Updates;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Office = Microsoft.Office.Core;
using Wpf = System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace KleiKodesh.Helpers.AddinUpdate
{
    // ======================================================================================
    // The add-in's own update check (no service)
    // ======================================================================================

    /// <summary>What is known about the update: a downloaded installer waiting, and the installed version.</summary>
    internal sealed class AddinUpdateStatus
    {
        /// <summary>A downloaded installer newer than the installed version is waiting.</summary>
        public bool Ready;
        /// <summary>Its version ("v1.2.3"); "" when nothing is ready.</summary>
        public string ReadyVersion = "";
        /// <summary>The installed copy's version; "" when unknown or not an installed copy.</summary>
        public string InstalledVersion = "";
        /// <summary>Check-now only: why the check could not run or failed; "" when it ran.</summary>
        public string CheckError = "";
    }

    /// <summary>
    /// The add-in updates itself, in Word's process: GitHub's latest release, its installer
    /// downloaded, the installer started on request. It does not use the Kitvei Hakodesh
    /// service, so the add-in also updates when Kitvei Hakodesh is not installed (then there
    /// is no service at all).
    ///
    /// It shares everything on disk with the service's updater (KitveiHakodeshService
    /// Updater.cs), so the two never clash: the same "updates" folder
    /// (install folder\KitveiHakodeshService\updates), the same file names
    /// (KleiKodeshSetup-TAG.exe, ".part" while downloading), the same installed-version
    /// stamp, and the same lock file (download.lock, held exclusively for a whole pass). When
    /// the other one holds the lock, this pass is skipped; a finished download of either is
    /// picked up by both, because what is ready is always read from disk.
    /// </summary>
    internal static class AddinUpdateClient
    {
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest";

        private static readonly Lazy<HttpClient> Http = new Lazy<HttpClient>(() =>
        {
            // .NET Framework does not offer TLS 1.2 by default everywhere; GitHub requires it.
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("KleiKodesh-WordAddin");
            return client;
        });

        /// <summary>Where installers are downloaded (the install folder holds the add-in's DLLs); the service's updater uses the same folder.</summary>
        public static string DownloadFolder =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KitveiHakodeshService", "updates");

        /// <summary>The installer's own stamp; "" when this is not an installed copy.</summary>
        public static string InstalledVersion() => UpdateInstallers.InstalledVersion(UserProfile.Hive) ?? "";

        /// <summary>
        /// Checks GitHub now and downloads a newer installer, so this can take minutes. Never
        /// throws: a failure comes back in <see cref="AddinUpdateStatus.CheckError"/>.
        /// </summary>
        public static async Task<AddinUpdateStatus> CheckNow(CancellationToken ct)
        {
            string installed = InstalledVersion();
            // Only an installed copy updates: a dev checkout or an unzipped copy must never start an installer.
            if (installed.Length == 0)
                return new AddinUpdateStatus { CheckError = "updates are checked only by the installed version" };

            string error = "";
            try { await FindOrDownloadAsync(installed, ct).ConfigureAwait(false); }
            catch (Exception ex) { error = ex.Message; }

            var ready = UpdateInstallers.ReadyInstaller(DownloadFolder, installed);
            return new AddinUpdateStatus
            {
                Ready = ready != null,
                ReadyVersion = ready?.Version ?? "",
                InstalledVersion = installed,
                CheckError = error,
            };
        }

        /// <summary>Starts the downloaded installer. Returns null on success, the reason otherwise.</summary>
        public static Task<string> Install(CancellationToken ct)
        {
            string installed = InstalledVersion();
            if (installed.Length == 0) return Task.FromResult("updates are installed only by the installed version");
            var ready = UpdateInstallers.ReadyInstaller(DownloadFolder, installed);
            if (ready == null) return Task.FromResult("no update is ready");
            // No arguments, ever: the installer is a release of its own. It asks to close Word itself.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ready.Value.Path) { UseShellExecute = true });
            return Task.FromResult<string>(null);
        }

        private static async Task FindOrDownloadAsync(string installed, CancellationToken ct)
        {
            FileStream gate = UpdateInstallers.TryTakeDownloadLock(DownloadFolder);
            // The service (or another Word) is checking right now; its download lands in the same
            // folder. Reported as not run, so the weekly stamp is not written and the next pass retries.
            if (gate == null) throw new InvalidOperationException(AddinUiLanguage.T("בדיקה אחרת כבר רצה, נסו שוב בעוד כמה דקות", "another check is already running, try again in a few minutes"));
            using (gate)
            {
                DeleteInstallers(f => UpdateInstallers.CompareVersions(UpdateInstallers.TagOf(f), installed) <= 0);

                string assetName, url = null;
                long size = 0;
                using (var response = await Http.Value.GetAsync(LatestReleaseApiUrl, ct).ConfigureAwait(false))
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw new HttpRequestException("no published release (404)");
                    response.EnsureSuccessStatusCode();
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using (var json = System.Text.Json.JsonDocument.Parse(body))
                    {
                        var root = json.RootElement;
                        string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                        if (tag.Length == 0 || UpdateInstallers.CompareVersions(tag, installed) <= 0) return;
                        // One installer since 2026-10-11: AnyCPU, no platform suffix.
                        assetName = "KleiKodeshSetup-" + tag + ".exe";
                        if (root.TryGetProperty("assets", out var assets))
                            foreach (var a in assets.EnumerateArray())
                                if (string.Equals(a.GetProperty("name").GetString(), assetName, StringComparison.OrdinalIgnoreCase))
                                {
                                    size = a.GetProperty("size").GetInt64();
                                    url = a.GetProperty("browser_download_url").GetString();
                                }
                        if (string.IsNullOrEmpty(url) || size <= 0) return;
                    }
                }

                string target = Path.Combine(DownloadFolder, assetName);
                DeleteInstallers(f => !string.Equals(f, target, StringComparison.OrdinalIgnoreCase));
                // Already downloaded (by either updater): the same name at the published size is that release.
                if (File.Exists(target) && new FileInfo(target).Length == size) return;

                string partial = target + ".part";
                try
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        // A stalled connection must not hang the pass.
                        timeout.CancelAfter(TimeSpan.FromMinutes(60));
                        using (var response = await Http.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                        {
                            response.EnsureSuccessStatusCode();
                            using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var destination = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                                await source.CopyToAsync(destination, 81920, timeout.Token).ConfigureAwait(false);
                        }
                    }
                    long length = new FileInfo(partial).Length;
                    if (length != size) throw new IOException($"download incomplete: {length} of {size} bytes");
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(partial, target);
                }
                finally
                {
                    try { if (File.Exists(partial)) File.Delete(partial); } catch { /* next pass retries */ }
                }
            }
        }

        private static void DeleteInstallers(Func<string, bool> which)
        {
            foreach (string file in Directory.EnumerateFiles(DownloadFolder, "KleiKodeshSetup-*.exe"))
                if (which(file))
                    try { File.Delete(file); } catch { /* locked by a running installer - next pass */ }
        }
    }

    // ======================================================================================
    // What is ready, and the weekly check
    // ======================================================================================

    /// <summary>How urgent the waiting update looks, the same ladder as the Kitvei Hakodesh sidebar button.</summary>
    internal enum AddinUpdateTone
    {
        /// <summary>Nothing is ready: no button.</summary>
        None,
        /// <summary>Just downloaded (blue).</summary>
        Ready,
        /// <summary>Waiting 2+ days (orange).</summary>
        Aging,
        /// <summary>Waiting 7+ days (red).</summary>
        Overdue,
    }

    /// <summary>
    /// What the add-in knows about the update.
    ///
    /// WHAT IS READY is never kept: it is read from disk when the ribbon asks (a few seconds'
    /// cache, no timer) with the same rule the service uses (Core's UpdateInstallers: a complete
    /// installer in the shared "updates" folder that is newer than the version the installer
    /// stamped in the registry). So a download the service finished shows up too. A stale button (installed from the app, say) is caught when the
    /// user clicks it: the click re-reads, tells the user, and the item drops out.
    ///
    /// GETTING it downloaded is the add-in's own job (AddinUpdateClient, in Word's process; no
    /// service is started). Nothing runs at Word's startup. When the user opens a FRESH side
    /// pane (the first one in a window, or another one beside it - not when an existing pane
    /// is merely shown again) <see cref="PaneOpened"/> runs: at most once per Word session, and
    /// only when a week has passed (stamp UpdateChecker/LastCheckUtc), it checks GitHub in the
    /// background and downloads a newer installer. No timer, no heartbeat. Between checks
    /// nothing runs.
    ///
    /// The tone ages like the app's button: the moment a version was first seen ready is saved
    /// (UpdateChecker/ReadySeen*), so it survives a Word restart.
    /// </summary>
    internal static class AddinUpdateState
    {
        private const string Section = "UpdateChecker";
        // Lets the pane that just opened finish its own start first.
        private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan CheckEvery = TimeSpan.FromDays(7);
        private static readonly TimeSpan CheckTimeout = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan AgingAfter = TimeSpan.FromDays(2);
        private static readonly TimeSpan OverdueAfter = TimeSpan.FromDays(7);

        private static readonly object Sync = new object();
        private static SynchronizationContext _ui;
        // True once this Word session has looked at whether a check is due. Touched on the UI thread only.
        private static bool _checkTried;
        private static string _readyVersion = "";
        private static string _installedVersion = "";
        private static DateTime _readAtUtc = DateTime.MinValue;
        // When the ready version was first seen (registry, read together with the disk, never per ribbon callback).
        private static DateTime _readySinceUtc = DateTime.UtcNow;

        /// <summary>Raised on Word's UI thread whenever the ready update changed.</summary>
        public static event Action Changed;

        /// <summary>The version of the downloaded installer waiting to run; "" when none (or updates are turned off).</summary>
        public static string ReadyVersion { get { ReadIfStale(); lock (Sync) return _readyVersion; } }

        /// <summary>The installed version the installer stamped; "" when unknown (not an installed copy).</summary>
        public static string InstalledVersion { get { ReadIfStale(); lock (Sync) return _installedVersion; } }

        public static bool AutomaticUpdatesTurnedOff =>
            SettingsManager.GetBool(Section, "TurnOffUpdates", false);

        public static AddinUpdateTone Tone
        {
            get
            {
                if (ReadyVersion.Length == 0) return AddinUpdateTone.None;
                DateTime since;
                lock (Sync) since = _readySinceUtc;
                TimeSpan age = DateTime.UtcNow - since;
                if (age >= OverdueAfter) return AddinUpdateTone.Overdue;
                if (age >= AgingAfter) return AddinUpdateTone.Aging;
                return AddinUpdateTone.Ready;
            }
        }

        /// <summary>
        /// Call on the UI thread each time a NEW side pane is created. Returns at once; the
        /// check (when one is due) runs on a background thread.
        /// </summary>
        public static void PaneOpened()
        {
            if (_checkTried) return;
            _checkTried = true;
            CaptureUi();
            Task.Run(CheckIfDue);
        }

        /// <summary>Remembers the calling (UI) thread, which <see cref="Changed"/> is raised on.</summary>
        public static void CaptureUi()
        {
            if (_ui == null)
                _ui = SynchronizationContext.Current ?? new System.Windows.Forms.WindowsFormsSynchronizationContext();
        }

        /// <summary>Reads what is ready again now (after a check, a click, the switch being flipped) and tells listeners if it moved.</summary>
        public static void Reload()
        {
            string before;
            lock (Sync) before = _readyVersion;
            Read();
            string after;
            lock (Sync) after = _readyVersion;
            if (before != after) RaiseChanged();
        }

        // Off the UI thread (the settings and registry reads included): is a check due, and if so run it.
        private static async Task CheckIfDue()
        {
            try
            {
                if (AutomaticUpdatesTurnedOff || !IsCheckDue()) return;
                // Lets the pane that just opened finish its own start first.
                await Task.Delay(StartDelay).ConfigureAwait(false);
                using (var timeout = new CancellationTokenSource(CheckTimeout))
                {
                    // Checks, downloads, answers.
                    AddinUpdateStatus status = await AddinUpdateClient.CheckNow(timeout.Token).ConfigureAwait(false);
                    // Stamped only when the check ran, so a failed one retries at the next Word session.
                    if (status.CheckError.Length == 0)
                        SettingsManager.Save(Section, "LastCheckUtc", DateTime.UtcNow.ToString("o"));
                }
            }
            catch
            {
                // Unexpected; the settings page's check-now says why when asked.
            }
            Reload();
        }

        private static bool IsCheckDue() =>
            !DateTime.TryParse(SettingsManager.Get(Section, "LastCheckUtc", ""), null,
                System.Globalization.DateTimeStyles.RoundtripKind, out DateTime last)
            || DateTime.UtcNow - last.ToUniversalTime() >= CheckEvery;

        private static void ReadIfStale()
        {
            bool stale;
            lock (Sync) stale = DateTime.UtcNow - _readAtUtc > CacheFor;
            if (stale) Read();
        }

        // Disk + registry only; never touches the service.
        private static void Read()
        {
            string installed = "";
            string ready = "";
            DateTime since = DateTime.UtcNow;
            try
            {
                installed = UpdateInstallers.InstalledVersion(UserProfile.Hive) ?? "";
                if (!AutomaticUpdatesTurnedOff && installed.Length > 0)
                {
                    // The folder both updaters (the add-in's and the service's) download into.
                    ready = UpdateInstallers.ReadyInstaller(AddinUpdateClient.DownloadFolder, installed)?.Version ?? "";
                    if (ready.Length > 0) since = FirstSeenUtc(ready);
                }
            }
            catch { /* unreadable right now: no button rather than a wrong one */ }

            lock (Sync)
            {
                _installedVersion = installed;
                _readyVersion = ready;
                _readySinceUtc = since;
                _readAtUtc = DateTime.UtcNow;
            }
        }

        /// <summary>Runs <paramref name="action"/> on Word's UI thread (the thread captured by <see cref="CaptureUi"/>).</summary>
        public static void PostToUi(Action action)
        {
            if (_ui != null) _ui.Post(_ => action(), null);
        }

        private static void RaiseChanged()
        {
            Action handler = Changed;
            if (handler == null || _ui == null) return;
            _ui.Post(_ => handler(), null);
        }

        // The moment this version was first seen ready (saved, so it survives a Word restart; a new version starts now).
        private static DateTime FirstSeenUtc(string version)
        {
            if (SettingsManager.Get(Section, "ReadySeenVersion", "") == version
                && DateTime.TryParse(SettingsManager.Get(Section, "ReadySeenAtUtc", ""), null,
                       System.Globalization.DateTimeStyles.RoundtripKind, out DateTime seen))
                return seen.ToUniversalTime();

            DateTime now = DateTime.UtcNow;
            SettingsManager.Save(Section, "ReadySeenVersion", version);
            SettingsManager.Save(Section, "ReadySeenAtUtc", now.ToString("o"));
            return now;
        }
    }

    // ======================================================================================
    // The pictures
    // ======================================================================================

    /// <summary>
    /// The update item's picture. A ribbon item cannot be tinted, so its colour is drawn into
    /// the image: a coloured disc with a white "download" arrow. Colours match the Kitvei
    /// Hakodesh sidebar button.
    /// </summary>
    internal static class AddinUpdateIcons
    {
        public static Color ColorOf(AddinUpdateTone tone)
        {
            switch (tone)
            {
                case AddinUpdateTone.Aging:   return Color.FromArgb(0xE8, 0x87, 0x1E);
                case AddinUpdateTone.Overdue: return Color.FromArgb(0xD1, 0x34, 0x38);
                default:                      return Color.FromArgb(0x00, 0x78, 0xD4);
            }
        }

        // Each picture is drawn once per tone (at most 3 small masters per process). Office is
        // only ever handed a COPY, so it can neither dispose nor change the master; a copy nobody
        // references is reclaimed by its finalizer, like every other ribbon image in this add-in.
        private static readonly Dictionary<AddinUpdateTone, Bitmap> Arrows = new Dictionary<AddinUpdateTone, Bitmap>();

        /// <summary>A coloured disc holding a white down arrow, 256x256 (a copy of the per-tone master).</summary>
        public static Bitmap Arrow(AddinUpdateTone tone)
        {
            lock (Arrows)
            {
                if (!Arrows.TryGetValue(tone, out Bitmap master)) Arrows[tone] = master = DrawArrow(tone);
                return (Bitmap)master.Clone();
            }
        }

        // Drawn large, like every other ribbon picture in this add-in (their PNGs are 256 px or
        // more): Word scales a picture DOWN smoothly for the screen's DPI, but a 32 px one is
        // scaled up and looks pixelated. Coordinates below are on a 32-unit grid, scaled to Size.
        private const int Size = 256;

        private static Bitmap DrawArrow(AddinUpdateTone tone)
        {
            float k = Size / 32f;
            var bitmap = new Bitmap(Size, Size);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var disc = new SolidBrush(ColorOf(tone)))
            using (var white = new SolidBrush(Color.White))
            using (var whitePen = new Pen(Color.White, 3f * k))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.FillEllipse(disc, 1 * k, 1 * k, 30 * k, 30 * k);
                g.FillRectangle(white, 14 * k, 7 * k, 4 * k, 10 * k);
                g.FillPolygon(white, new[]
                {
                    new PointF(9 * k, 15 * k), new PointF(23 * k, 15 * k), new PointF(16 * k, 23 * k),
                });
                g.DrawLine(whitePen, 9 * k, 26 * k, 23 * k, 26 * k);
            }
            return bitmap;
        }

    }

    // ======================================================================================
    // The texts
    // ======================================================================================

    /// <summary>Every update text the user sees (ribbon button, install prompt, settings section), Hebrew and English.</summary>
    internal static class AddinUpdateText
    {
        public static string Label(string version) => AddinUiLanguage.T(
            "עדכון זמין - גרסה " + version,
            "Update available - version " + version);

        public static string InstallPrompt(string version) => AddinUiLanguage.T(
            "גרסה " + version + " מוכנה להתקנה.\nהתוכנית תבקש לסגור את וורד כדי להתקין. להתקין עכשיו?",
            "Version " + version + " is ready to install.\nThe installer will ask to close Word. Install now?");

        public static string NoLongerAvailable => AddinUiLanguage.T(
            "העדכון כבר הותקן או שאינו זמין יותר.",
            "This update was already installed or is no longer available.");

        public static string InstallFailed(string reason) => AddinUiLanguage.T(
            "לא ניתן היה להפעיל את ההתקנה: " + reason,
            "The installer could not be started: " + reason);

        // ── settings section ──
        public static string Header => AddinUiLanguage.T("עדכונים", "Updates");
        public static string CheckNow => AddinUiLanguage.T("בדוק עדכונים עכשיו", "Check for updates now");
        public static string Checking => AddinUiLanguage.T("בודק...", "Checking...");
        public static string InstallNow => AddinUiLanguage.T("התקן עדכון", "Install update");
        public static string TurnOff => AddinUiLanguage.T("כבה בדיקת עדכונים אוטומטית", "Turn off automatic update checks");

        public static string Installed(string version) => AddinUiLanguage.T(
            "גרסה מותקנת: " + version, "Installed version: " + version);

        public static string Ready(string version) => AddinUiLanguage.T(
            "גרסה " + version + " הורדה ומוכנה להתקנה", "Version " + version + " is downloaded and ready to install");

        public static string UpToDate => AddinUiLanguage.T("התוכנה מעודכנת", "You are up to date");
        public static string Idle => AddinUiLanguage.T("העדכונים נבדקים אוטומטית פעם בשבוע", "Updates are checked automatically once a week");
        public static string TurnedOff => AddinUiLanguage.T("בדיקת העדכונים האוטומטית כבויה", "Automatic update checks are turned off");

        public static string Failed(string reason) => AddinUiLanguage.T(
            "בדיקת העדכונים נכשלה" + Suffix(reason), "The update check failed" + Suffix(reason));

        private static string Suffix(string reason) => string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")";
    }

    // ======================================================================================
    // The install click
    // ======================================================================================

    /// <summary>
    /// The ribbon update button's click: re-read what is ready (the button may be stale),
    /// ask the user, and start the downloaded installer (which itself asks
    /// to close Word).
    /// </summary>
    internal static class AddinUpdateInstall
    {
        public static void PromptAndInstall()
        {
            // The item was drawn from a cached read. Read again: an update installed from the
            // app, or one that is not newer than the installed version, is no longer offered
            // (and the same re-read drops the ribbon item).
            AddinUpdateState.Reload();
            string version = AddinUpdateState.ReadyVersion;
            if (version.Length == 0)
            {
                Show(AddinUpdateText.NoLongerAvailable, WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }

            if (Show(AddinUpdateText.InstallPrompt(version), WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Question)
                != WinForms.DialogResult.Yes)
                return;

            // Off the UI thread, so Word never waits on the disk. Only the failure message comes back to the UI thread.
            Task.Run(async () =>
            {
                string error;
                try
                {
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                        error = await AddinUpdateClient.Install(timeout.Token).ConfigureAwait(false);
                }
                catch (Exception ex) { error = ex.Message; }

                AddinUpdateState.PostToUi(() =>
                {
                    AddinUpdateState.Reload();
                    if (error != null)
                        Show(AddinUpdateText.InstallFailed(error), WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                });
            });
        }

        private static WinForms.DialogResult Show(string text, WinForms.MessageBoxButtons buttons, WinForms.MessageBoxIcon icon) =>
            WinForms.MessageBox.Show(text, KleiKodesh.Ribbon.AddinText.Label("Update"), buttons, icon,
                WinForms.MessageBoxDefaultButton.Button1,
                AddinUiLanguage.IsEnglish ? 0 : WinForms.MessageBoxOptions.RtlReading | WinForms.MessageBoxOptions.RightAlign);
    }

    // ======================================================================================
    // The ribbon's part
    // ======================================================================================

    /// <summary>
    /// The update's part of the ribbon: the "Update available" menu item (visible only while an
    /// update is ready, drawn in the tone's colour). That item is the only signal: the main
    /// button is left alone. The ribbon callbacks (KeliKodeshRibbon) are one-line calls into here.
    /// </summary>
    internal static class AddinUpdateRibbon
    {
        /// <summary>Redraws the ribbon whenever the ready update changes.</summary>
        public static void Attach(Office.IRibbonUI ribbon)
        {
            AddinUpdateState.CaptureUi();
            AddinUpdateState.Changed += () =>
            {
                try { ribbon?.Invalidate(); } catch { }
            };
        }

        /// <summary>The "Update available" item's picture: an arrow in the colour of how long the update has waited.</summary>
        public static Image Image()
        {
            AddinUpdateTone tone = AddinUpdateState.Tone;
            return AddinUpdateIcons.Arrow(tone == AddinUpdateTone.None ? AddinUpdateTone.Ready : tone);
        }

        public static bool IsVisible() => AddinUpdateState.Tone != AddinUpdateTone.None;

        public static string Label() => AddinUpdateText.Label(AddinUpdateState.ReadyVersion);
    }

    // ======================================================================================
    // The settings view's "Updates" section
    // ======================================================================================

    /// <summary>
    /// The "Updates" section of the settings view: installed version, status line, "Check for
    /// updates now", "Install update" (only while one is ready) and the turn-off checkbox. The
    /// view only creates it with its controls and calls <see cref="ApplyTexts"/> on a language switch.
    /// </summary>
    internal sealed class AddinUpdateSettingsSection
    {
        private readonly Wpf.TextBlock _header, _installedText, _statusText;
        private readonly Wpf.Button _installButton, _checkButton;
        private readonly Wpf.CheckBox _turnOff;

        private bool _isChecking;
        // What the last check-now said (error or "up to date"); "" before any check.
        private string _checkResultText = "";

        public AddinUpdateSettingsSection(Wpf.TextBlock header, Wpf.TextBlock installedText, Wpf.TextBlock statusText, Wpf.Button installButton,
            Wpf.Button checkButton, Wpf.CheckBox turnOff)
        {
            _header = header; _installedText = installedText; _statusText = statusText;
            _installButton = installButton; _checkButton = checkButton; _turnOff = turnOff;

            // The checkbox is the same registry value the service's updater reads.
            _turnOff.IsChecked = AddinUpdateState.AutomaticUpdatesTurnedOff;
            _turnOff.Checked   += (_, __) => SetTurnedOff(true);
            _turnOff.Unchecked += (_, __) => SetTurnedOff(false);

            _checkButton.Click += async (_, __) => await CheckNow();
            _installButton.Click += (_, __) =>
            {
                AddinUpdateInstall.PromptAndInstall();
                ShowStatus();
            };
            ApplyTexts();
        }

        /// <summary>Sets every text in the current UI language and refreshes the status.</summary>
        public void ApplyTexts()
        {
            _header.Text = AddinUpdateText.Header;
            _turnOff.Content = AddinUpdateText.TurnOff;
            _installButton.Content = AddinUpdateText.InstallNow;
            _checkButton.Content = _isChecking ? AddinUpdateText.Checking : AddinUpdateText.CheckNow;
            ShowStatus();
        }

        private void SetTurnedOff(bool turnedOff)
        {
            SettingsManager.Save("UpdateChecker", "TurnOffUpdates", turnedOff);
            AddinUpdateState.Reload();
            ShowStatus();
        }

        // Can take minutes: a newer release is downloaded before this returns.
        private async Task CheckNow()
        {
            if (_isChecking) return;
            _isChecking = true;
            _checkResultText = "";
            _checkButton.IsEnabled = false;
            _checkButton.Content = AddinUpdateText.Checking;
            try
            {
                // A stalled connection must not leave the button stuck on "Checking..."
                AddinUpdateStatus status;
                using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15)))
                    status = await Task.Run(() => AddinUpdateClient.CheckNow(timeout.Token));
                AddinUpdateState.Reload();
                _checkResultText = status.CheckError.Length > 0
                    ? AddinUpdateText.Failed(status.CheckError)
                    : AddinUpdateState.ReadyVersion.Length > 0 ? "" : AddinUpdateText.UpToDate;
            }
            catch
            {
                _checkResultText = AddinUpdateText.Failed("");
            }
            _isChecking = false;
            _checkButton.Content = AddinUpdateText.CheckNow;
            ShowStatus();
        }

        private void ShowStatus()
        {
            string installed = AddinUpdateState.InstalledVersion;
            _installedText.Text = AddinUpdateText.Installed(installed.Length > 0 ? installed : "-");

            string ready = AddinUpdateState.ReadyVersion;
            _installButton.Visibility = ready.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            // A check is refused while updates are turned off.
            bool off = _turnOff.IsChecked == true;
            _checkButton.IsEnabled = !_isChecking && !off;

            _statusText.Text =
                ready.Length > 0 ? AddinUpdateText.Ready(ready)
                : off ? AddinUpdateText.TurnedOff
                : _checkResultText.Length > 0 ? _checkResultText
                : AddinUpdateText.Idle;
        }
    }
}
