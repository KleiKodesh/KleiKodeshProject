using KleiKodesh.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KleiKodesh.ToolRail
{
    /// <summary>
    /// The user's own rail items, read from %APPDATA%\KleiKodesh\tool-rail.json.
    /// No code change is needed to add one. Three kinds:
    ///
    ///   url     - a web page in the shared pane, kept alive like any tool
    ///   program - a desktop program (e.g. a transcription program), started or
    ///             brought forward and snapped beside Word. Not embedded in the pane:
    ///             parenting another process's window into Word joins their input
    ///             queues, so a program that hangs would hang Word with it.
    ///   macro   - a Word macro, by the name Application.Run takes
    ///
    /// The file is re-read when it changes, so edits show on the next rail refresh.
    /// See ToolRail/README.md for the format.
    /// </summary>
    internal static class ExternalTools
    {
        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KleiKodesh", "tool-rail.json");

        static List<ToolEntry> _cached = new List<ToolEntry>();
        static DateTime _cachedStamp = DateTime.MinValue;
        static DateTime _reportedStamp = DateTime.MinValue;

        /// <summary>
        /// "Edit the external tools list" on the ribbon: opens the file in Notepad,
        /// first creating it with commented-out examples if there is none yet - so the
        /// user never has to find the folder or guess the format.
        /// </summary>
        public static void OpenFileForEditing()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.WriteAllText(FilePath, StarterFile, new System.Text.UTF8Encoding(false));
                }
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + FilePath + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Ribbon.AddinText.Label("ExternalTools"));
            }
        }

        // Every example is a comment, so the starter file adds nothing until edited.
        const string StarterFile =
            "// כלים חיצוניים לכלי קודש. מחק את // בתחילת שורה כדי להפעיל אותה, ושמור.\r\n" +
            "// External tools for Klei Kodesh. Remove the // at the start of a line to use it, then save.\r\n" +
            "// type: url (web page in the side pane), program (opens beside Word), macro (a Word macro)\r\n" +
            "{\r\n" +
            "  \"tools\": [\r\n" +
            "    // { \"id\": \"sefaria\", \"title\": \"ספריא\", \"type\": \"url\", \"url\": \"https://www.sefaria.org.il\" },\r\n" +
            "    // { \"id\": \"tamlil\", \"title\": \"תמליל\", \"type\": \"program\", \"path\": \"C:\\\\Program Files\\\\Tamlil\\\\Tamlil.exe\" },\r\n" +
            "    // { \"id\": \"mymacro\", \"title\": \"המאקרו שלי\", \"type\": \"macro\", \"macro\": \"MyMacro\" },\r\n" +
            "  ]\r\n" +
            "}\r\n";

        public static List<ToolEntry> Load()
        {
            try
            {
                var stamp = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
                if (stamp == _cachedStamp) return _cached;
                _cachedStamp = stamp;
                _cached = stamp == DateTime.MinValue ? new List<ToolEntry>() : Parse(File.ReadAllText(FilePath));
            }
            catch (Exception ex)
            {
                _cached = new List<ToolEntry>();
                // Say once per version of the file, not on every rail refresh.
                if (_reportedStamp != _cachedStamp)
                {
                    _reportedStamp = _cachedStamp;
                    MessageBox.Show(
                        AddinUiLanguage.T($"לא ניתן לקרוא את קובץ הכלים החיצוניים:\n{FilePath}\n\n{ex.Message}",
                                          $"Could not read the external tools file:\n{FilePath}\n\n{ex.Message}"),
                        Ribbon.AddinText.Label("KleiKodesh_Main"));
                }
            }
            return _cached;
        }

        // Accepts { "tools": [ ... ] } or a bare array. An item with a missing or
        // unknown type, or without what its type needs, is skipped.
        static List<ToolEntry> Parse(string json)
        {
            var list = new List<ToolEntry>();
            using (var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            }))
            {
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("tools", out var tools))
                    root = tools;
                if (root.ValueKind != JsonValueKind.Array) return list;

                var seen = new HashSet<string>();
                foreach (var item in root.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var entry = ToEntry(item);
                    if (entry != null && seen.Add(entry.Id))
                        list.Add(entry);
                }
            }
            return list;
        }

        static ToolEntry ToEntry(JsonElement item)
        {
            string type = item.GetStringProperty("type")?.Trim().ToLowerInvariant();
            string title = item.GetStringProperty("title", "name");
            string id = item.GetStringProperty("id") ?? title;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title)) return null;

            var entry = new ToolEntry
            {
                // Prefixed so a user item can never take a built-in tool's settings keys.
                Id = "ext:" + id.Trim(),
                External = true,
                Url = item.GetStringProperty("url"),
                Path = Expand(item.GetStringProperty("path")),
                Arguments = item.GetStringProperty("arguments", "args"),
                Macro = item.GetStringProperty("macro"),
                Snap = item.GetBoolProperty("snap") ?? true,
            };
            string tip = item.GetStringProperty("tip", "tooltip");
            string iconPath = Expand(item.GetStringProperty("icon"));
            entry.Label = () => title;
            entry.Tip = () => tip;

            if (item.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number && w.TryGetInt32(out int width))
                entry.DefaultWidth = Math.Max(200, Math.Min(width, 2000));

            switch (type)
            {
                case "url":
                    if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri) ||
                        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeFile))
                        return null;
                    entry.Kind = ToolKind.Pane;
                    string url = uri.AbsoluteUri;
                    entry.CreateControl = () => new RailWebView(url);
                    entry.LoadIcon = () => ToolCatalog.LoadImageFile(iconPath);
                    return entry;

                case "program":
                    if (string.IsNullOrWhiteSpace(entry.Path)) return null;
                    entry.Kind = ToolKind.Program;
                    string exe = entry.Path;
                    entry.LoadIcon = () => ToolCatalog.LoadImageFile(iconPath) ?? ProgramIcon(exe);
                    return entry;

                case "macro":
                    if (string.IsNullOrWhiteSpace(entry.Macro)) return null;
                    entry.Kind = ToolKind.Macro;
                    entry.LoadIcon = () => ToolCatalog.LoadImageFile(iconPath);
                    return entry;

                default:
                    return null;
            }
        }

        static string Expand(string path) =>
            string.IsNullOrWhiteSpace(path) ? null : Environment.ExpandEnvironmentVariables(path.Trim());

        static Image ProgramIcon(string exe)
        {
            try
            {
                if (!File.Exists(exe)) return null;
                using (var icon = Icon.ExtractAssociatedIcon(exe))
                    return icon?.ToBitmap();
            }
            catch { return null; }
        }

        // ── macro ─────────────────────────────────────────────────────────────

        public static void RunMacro(ToolEntry entry)
        {
            try
            {
                Globals.ThisAddIn.Application.Run(entry.Macro);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    AddinUiLanguage.T($"הפעלת המאקרו \"{entry.Macro}\" נכשלה:\n{ex.Message}",
                                      $"Running the macro \"{entry.Macro}\" failed:\n{ex.Message}"),
                    entry.Label());
            }
        }

        // ── program ───────────────────────────────────────────────────────────

        /// <summary>
        /// Starts the program, or brings forward the copy already running, and snaps
        /// it beside Word on the side the pane is docked. Word's own window handle and
        /// the side are read up front: the wait for the program's window must not
        /// touch Word through COM after an await.
        /// </summary>
        public static async void RunProgram(ToolEntry entry, bool dockedLeft)
        {
            try
            {
                if (!File.Exists(entry.Path))
                {
                    MessageBox.Show(
                        AddinUiLanguage.T($"התוכנה לא נמצאה:\n{entry.Path}", $"The program was not found:\n{entry.Path}"),
                        entry.Label());
                    return;
                }

                var wordHwnd = new IntPtr(TaskPaneManager.ActiveWindowHandle());

                var process = FindRunning(entry.Path);
                if (process == null)
                {
                    process = Process.Start(new ProcessStartInfo(entry.Path, entry.Arguments ?? "")
                    {
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(entry.Path),
                    });
                }

                var programHwnd = await WaitForMainWindow(process, entry.Path);
                if (programHwnd == IntPtr.Zero) return;

                if (entry.Snap && wordHwnd != IntPtr.Zero)
                    SnapBeside(programHwnd, wordHwnd, dockedLeft);

                if (IsIconic(programHwnd)) ShowWindow(programHwnd, SW_RESTORE);
                SetForegroundWindow(programHwnd);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    AddinUiLanguage.T($"הפעלת התוכנה נכשלה:\n{ex.Message}", $"Starting the program failed:\n{ex.Message}"),
                    entry.Label());
            }
        }

        static Process FindRunning(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (!string.Equals(p.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                catch
                {
                    // An elevated process hides its path; the name alone will do.
                }
                if (p.MainWindowHandle != IntPtr.Zero) return p;
            }
            return null;
        }

        // A freshly started program takes a moment to show its window; a program that
        // hands off to an already-running copy returns no process at all.
        static async Task<IntPtr> WaitForMainWindow(Process process, string path)
        {
            for (int i = 0; i < 60; i++)
            {
                try
                {
                    if (process != null && !process.HasExited)
                    {
                        process.Refresh();
                        if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
                    }
                    else
                    {
                        var running = FindRunning(path);
                        if (running != null) return running.MainWindowHandle;
                    }
                }
                catch { }
                await Task.Delay(250);
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// The program takes the screen edge on the pane's side, at its own width
        /// (clamped to a quarter..half of the screen); Word takes the rest.
        /// </summary>
        static void SnapBeside(IntPtr program, IntPtr word, bool dockedLeft)
        {
            var area = Screen.FromHandle(word).WorkingArea;

            GetWindowRect(program, out var pr);
            int width = pr.Right - pr.Left;
            if (IsZoomed(program) || width <= 0) width = area.Width * 35 / 100;
            width = Math.Max(area.Width / 4, Math.Min(width, area.Width / 2));

            if (IsZoomed(program) || IsIconic(program)) ShowWindow(program, SW_RESTORE);
            if (IsZoomed(word) || IsIconic(word)) ShowWindow(word, SW_RESTORE);

            int programX = dockedLeft ? area.Left : area.Right - width;
            int wordX = dockedLeft ? area.Left + width : area.Left;

            SetWindowPos(word, IntPtr.Zero, wordX, area.Top, area.Width - width, area.Height, SWP_NOZORDER | SWP_NOACTIVATE);
            SetWindowPos(program, IntPtr.Zero, programX, area.Top, width, area.Height, SWP_NOZORDER);
        }

        const int SW_RESTORE = 9;
        const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
