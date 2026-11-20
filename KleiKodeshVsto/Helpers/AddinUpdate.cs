// ALL of the Word add-in's update logic lives in this one file. The add-in does not update
// itself and does not run the service for a whole session: it starts the KitveiHakodesh service
// for one background check when a fresh side pane opens (at most weekly), reads what the service
// downloaded straight from disk and the registry, and shows an "Update available" ribbon button
// (plus the settings view's "Updates" section). Other files only make one-line calls into here:
//   TaskpaneManager.OnPaneCreated   -> AddinUpdateState.PaneOpened()
//   KeliKodeshRibbon                -> AddinUpdateRibbon
//   AddinCommands "Update"          -> AddinUpdateInstall.PromptAndInstall()
//   RibbonSettingsView              -> AddinUpdateSettingsSection
using KitveiHakodesh.Core.Settings;
using KitveiHakodesh.Core.Updates;
using KitveiHakodeshLib.Service;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Office = Microsoft.Office.Core;
using Wpf = System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace KleiKodesh.Helpers.AddinUpdate
{
    // ======================================================================================
    // The wire format (a tiny MessagePack)
    // ======================================================================================

    /// <summary>
    /// Just enough MessagePack for the update ops: one request shape (<c>{ Op }</c>, no args)
    /// and a reader for the reply. The add-in carries no MessagePack package (the service
    /// pipe library is bytes-only on purpose), so this is written by hand, like the owner
    /// request in KitveiHakodeshLib's KitveiHakodeshServiceOwnership.
    ///
    /// Reader result types: null, bool, long, double, string, byte[] (bin), List&lt;object&gt;,
    /// and Dictionary&lt;string, object&gt; (keys compared ignoring case, so the service's
    /// PascalCase or camelCase naming both work).
    /// </summary>
    internal static class MsgPackLite
    {
        /// <summary>The request <c>{ Op: op }</c>. The op name must be shorter than 32 bytes.</summary>
        public static byte[] OpRequest(string op)
        {
            byte[] name = Encoding.ASCII.GetBytes(op);
            if (name.Length >= 32) throw new ArgumentException("op name too long for a fixstr", nameof(op));

            var bytes = new byte[1 + 3 + 1 + name.Length];
            int i = 0;
            bytes[i++] = 0x81;                                   // fixmap(1)
            bytes[i++] = 0xA2; bytes[i++] = (byte)'O'; bytes[i++] = (byte)'p';
            bytes[i++] = (byte)(0xA0 | name.Length);             // fixstr
            Buffer.BlockCopy(name, 0, bytes, i, name.Length);
            return bytes;
        }

        public static object Read(byte[] data)
        {
            var reader = new Reader(data);
            return reader.ReadValue();
        }

        public static Dictionary<string, object> ReadMap(byte[] data)
        {
            return Read(data) as Dictionary<string, object>
                ?? throw new FormatException("The service reply is not a map.");
        }

        public static string GetString(Dictionary<string, object> map, string key) =>
            map.TryGetValue(key, out object v) ? v as string ?? "" : "";

        public static bool GetBool(Dictionary<string, object> map, string key) =>
            map.TryGetValue(key, out object v) && v is bool b && b;

        private sealed class Reader
        {
            private readonly byte[] _b;
            private int _i;

            public Reader(byte[] data) { _b = data; }

            public object ReadValue()
            {
                byte t = _b[_i++];
                if (t <= 0x7F) return (long)t;
                if (t >= 0xE0) return (long)(sbyte)t;
                if ((t & 0xE0) == 0xA0) return ReadString(t & 0x1F);
                if ((t & 0xF0) == 0x80) return ReadMapOf(t & 0x0F);
                if ((t & 0xF0) == 0x90) return ReadArrayOf(t & 0x0F);

                switch (t)
                {
                    case 0xC0: return null;
                    case 0xC2: return false;
                    case 0xC3: return true;
                    case 0xC4: return ReadBytes(_b[_i++]);
                    case 0xC5: return ReadBytes(ReadUnsigned(2));
                    case 0xC6: return ReadBytes(ReadUnsigned(4));
                    case 0xCA: return (double)BitConverter.ToSingle(ReverseBytes(4), 0);
                    case 0xCB: return BitConverter.ToDouble(ReverseBytes(8), 0);
                    case 0xCC: return (long)_b[_i++];
                    case 0xCD: return (long)ReadUnsigned(2);
                    case 0xCE: return (long)ReadUnsigned(4);
                    case 0xCF: return (long)ReadUnsigned(8);
                    case 0xD0: return (long)(sbyte)_b[_i++];
                    case 0xD1: return (long)(short)ReadUnsigned(2);
                    case 0xD2: return (long)(int)ReadUnsigned(4);
                    case 0xD3: return (long)ReadUnsigned(8);
                    case 0xD9: return ReadString(_b[_i++]);
                    case 0xDA: return ReadString(ReadUnsigned(2));
                    case 0xDB: return ReadString(ReadUnsigned(4));
                    case 0xDC: return ReadArrayOf(ReadUnsigned(2));
                    case 0xDD: return ReadArrayOf(ReadUnsigned(4));
                    case 0xDE: return ReadMapOf(ReadUnsigned(2));
                    case 0xDF: return ReadMapOf(ReadUnsigned(4));
                    default: throw new FormatException("Unsupported MessagePack type 0x" + t.ToString("X2"));
                }
            }

            private int ReadUnsigned(int byteCount)
            {
                ulong v = 0;
                for (int k = 0; k < byteCount; k++) v = (v << 8) | _b[_i++];
                return unchecked((int)v);
            }

            private byte[] ReverseBytes(int count)
            {
                var copy = new byte[count];
                for (int k = 0; k < count; k++) copy[k] = _b[_i + count - 1 - k];
                _i += count;
                return copy;
            }

            private string ReadString(int length)
            {
                string s = Encoding.UTF8.GetString(_b, _i, length);
                _i += length;
                return s;
            }

            private byte[] ReadBytes(int length)
            {
                var copy = new byte[length];
                Buffer.BlockCopy(_b, _i, copy, 0, length);
                _i += length;
                return copy;
            }

            private List<object> ReadArrayOf(int count)
            {
                var list = new List<object>(count);
                for (int k = 0; k < count; k++) list.Add(ReadValue());
                return list;
            }

            private Dictionary<string, object> ReadMapOf(int count)
            {
                var map = new Dictionary<string, object>(count, StringComparer.OrdinalIgnoreCase);
                for (int k = 0; k < count; k++)
                {
                    string key = ReadValue() as string ?? "";
                    map[key] = ReadValue();
                }
                return map;
            }
        }
    }

    // ======================================================================================
    // The service pipe client
    // ======================================================================================

    /// <summary>What the service says about the update: a downloaded installer waiting, and the installed version.</summary>
    internal sealed class AddinUpdateStatus
    {
        public bool Ready;
        /// <summary>The version of the downloaded installer; "" when none is ready.</summary>
        public string ReadyVersion = "";
        /// <summary>The installed copy's version; "" when unknown or not an installed copy.</summary>
        public string InstalledVersion = "";
        /// <summary>Check-now only: why the check could not run or failed; "" when it ran.</summary>
        public string CheckError = "";
    }

    /// <summary>
    /// The add-in's side of the KitveiHakodesh service's self-update (the service owns all of
    /// it: weekly GitHub check, download, launching the installer). Two calls over the
    /// service pipe, the same ops the app's settings page uses: checkForUpdateNow and
    /// installUpdate. Both start the installed service first when it is not running.
    /// </summary>
    internal static class AddinUpdateClient
    {
        private static readonly KitveiHakodeshServicePipeChannel Pipe = new KitveiHakodeshServicePipeChannel();
        private static readonly KitveiHakodeshServiceLauncher Launcher = new KitveiHakodeshServiceLauncher();

        // Starts the installed service when it is not there (no admin prompt).
        private static readonly IKitveiHakodeshServiceChannel StartingPipe =
            new KitveiHakodeshServiceSelfStartingChannel(Pipe, Launcher);

        /// <summary>
        /// Runs the weekly pass now, starting the service if needed. A newer release is
        /// downloaded before the reply, so this can take minutes.
        /// </summary>
        public static async Task<AddinUpdateStatus> CheckNow(CancellationToken ct)
        {
            // Only the INSTALLED Windows service updates. Without it (a dev checkout, a portable
            // copy) starting a service here would spawn a console copy tied to Word's process
            // that lives as long as Word - for nothing, as it would refuse to update anyway.
            if (!KitveiHakodeshServiceLauncher.IsRegistered())
                return new AddinUpdateStatus { CheckError = "updates are checked only by the installed version" };
            // If the service was not running, THIS call started it, and a started service begins its
            // background index builds and then stays up until they finish - for a program that
            // never uses them. So when the check is over, ask it to go away again; it refuses
            // while any program owns it (a pane, another program), so it never cuts those off.
            bool startedByThisCall = !Launcher.IsListening();
            try
            {
                return ToStatus(await Call(StartingPipe, "checkForUpdateNow", ct).ConfigureAwait(false));
            }
            finally
            {
                if (startedByThisCall) await AskServiceToStopIfUnowned().ConfigureAwait(false);
            }
        }

        // Best effort and bounded: an older service without the op, or one already gone, simply
        // follows its own stop rules.
        private static async Task AskServiceToStopIfUnowned()
        {
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    await Call(Pipe, "stopIfUnowned", timeout.Token).ConfigureAwait(false);
            }
            catch { }
        }

        /// <summary>Asks the service to start the downloaded installer. Returns null on success, the reason otherwise.</summary>
        public static async Task<string> Install(CancellationToken ct)
        {
            // Same rule as CheckNow: never start a service for a copy that is not installed.
            if (!KitveiHakodeshServiceLauncher.IsRegistered())
                return "updates are installed only by the installed version";
            Dictionary<string, object> result = await Call(StartingPipe, "installUpdate", ct).ConfigureAwait(false);
            if (MsgPackLite.GetBool(result, "Ok")) return null;
            string error = MsgPackLite.GetString(result, "Error");
            return string.IsNullOrEmpty(error) ? "unknown error" : error;
        }

        private static async Task<Dictionary<string, object>> Call(
            IKitveiHakodeshServiceChannel channel, string op, CancellationToken ct)
        {
            byte[] reply = await channel.CallAsync(MsgPackLite.OpRequest(op), ct).ConfigureAwait(false);
            Dictionary<string, object> envelope = MsgPackLite.ReadMap(reply);
            if (!MsgPackLite.GetBool(envelope, "Ok"))
                throw new InvalidOperationException(MsgPackLite.GetString(envelope, "Error"));
            return envelope.TryGetValue("Result", out object bytes) && bytes is byte[] payload
                ? MsgPackLite.ReadMap(payload)
                : new Dictionary<string, object>();
        }

        private static AddinUpdateStatus ToStatus(Dictionary<string, object> result) => new AddinUpdateStatus
        {
            Ready = MsgPackLite.GetBool(result, "Ready"),
            ReadyVersion = MsgPackLite.GetString(result, "Version"),
            InstalledVersion = MsgPackLite.GetString(result, "InstalledVersion"),
            CheckError = MsgPackLite.GetString(result, "CheckError"),
        };
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
    /// installer in the service's "updates" folder that is newer than the version the installer
    /// stamped in the registry). So it needs no service, and a download the app's service
    /// finished shows up too. A stale button (installed from the app, say) is caught when the
    /// user clicks it: the click re-reads, tells the user, and the item drops out.
    ///
    /// GETTING it downloaded is the service's job, and it must NOT run for a whole Word session
    /// just for that. Nothing runs at Word's startup. When the user opens a FRESH side pane
    /// (the first one in a window, or another one beside it - not when an existing pane is
    /// merely shown again) <see cref="PaneOpened"/> runs: at most once per Word session, and
    /// only when a week has passed (stamp UpdateChecker/LastCheckUtc), it starts the service in
    /// the background and asks it to check now (a download included). Word is never registered
    /// as one of the service's owners, so there is nothing to release and nothing that can fail
    /// to be released. Two rules end the service, whichever applies: when THIS call started it,
    /// the add-in asks it to stop right after the check (stopIfUnowned: about 10 s later, even
    /// while its index builds run, unless a program owns it or a download is still running -
    /// it then keeps looking until the download ends); when it was already running for someone
    /// else, that program owns it and the service's own rules apply (it stops itself once
    /// nobody owns it and its background work is done). No timer, no heartbeat, no event
    /// subscription is created here. Between checks nothing runs.
    ///
    /// The tone ages like the app's button: the moment a version was first seen ready is saved
    /// (UpdateChecker/ReadySeen*), so it survives a Word restart.
    /// </summary>
    internal static class AddinUpdateState
    {
        private const string Section = "UpdateChecker";
        // Lets the pane that just opened make its own first requests to the service first.
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
                // Lets the pane that just opened make its own first requests to the service first.
                await Task.Delay(StartDelay).ConfigureAwait(false);
                using (var timeout = new CancellationTokenSource(CheckTimeout))
                {
                    // Starts the installed service if needed, checks, downloads, answers.
                    AddinUpdateStatus status = await AddinUpdateClient.CheckNow(timeout.Token).ConfigureAwait(false);
                    // Stamped only when the check ran, so a failed one retries at the next Word session.
                    if (status.CheckError.Length == 0)
                        SettingsManager.Save(Section, "LastCheckUtc", DateTime.UtcNow.ToString("o"));
                }
            }
            catch
            {
                // The service could not be brought up or reached: the settings page's check-now
                // says why when asked.
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
                string exe = KitveiHakodeshServiceLauncher.InstalledExecutable();
                if (!AutomaticUpdatesTurnedOff && installed.Length > 0 && exe != null)
                {
                    // The installed service keeps its downloads in "updates" beside its own exe.
                    string folder = Path.Combine(Path.GetDirectoryName(exe), "updates");
                    ready = UpdateInstallers.ReadyInstaller(folder, installed)?.Version ?? "";
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

        public static string ServiceUnavailable => AddinUiLanguage.T(
            "לא ניתן להגיע לשירות כתבי הקודש", "The Kitvei Hakodesh service could not be reached");

        private static string Suffix(string reason) => string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")";
    }

    // ======================================================================================
    // The install click
    // ======================================================================================

    /// <summary>
    /// The ribbon update button's click: re-read what is ready (the button may be stale),
    /// ask the user, and have the service start the downloaded installer (which itself asks
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

            // Off the UI thread: with the service stopped, the call first starts it (up to ~20 s),
            // and Word must not freeze meanwhile. Only the failure message comes back to the UI thread.
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

            // The checkbox is the same registry value the service reads.
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

        // Can take minutes: a newer release is downloaded before the service answers.
        private async Task CheckNow()
        {
            if (_isChecking) return;
            _isChecking = true;
            _checkResultText = "";
            _checkButton.IsEnabled = false;
            _checkButton.Content = AddinUpdateText.Checking;
            try
            {
                // A wedged service must not leave the button stuck on "Checking...".
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
                _checkResultText = AddinUpdateText.ServiceUnavailable;
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
