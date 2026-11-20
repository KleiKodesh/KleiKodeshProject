using OfficePaneChrome;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using DockPosition = Microsoft.Office.Core.MsoCTPDockPosition;
using Word = Microsoft.Office.Interop.Word;

namespace KleiKodesh.Helpers
{
    /// <summary>
    /// The add-in's side panes. Every ribbon pane is a view shuffled into the shared side pane of the
    /// document window (OfficePaneChrome.SidePanes): one Office task pane per window, the views inside it
    /// keep their state, the title strip carries the view selector and the pop-out button, Office's X
    /// closes the side pane for real. This class only plugs the add-in's specifics into SidePanes:
    /// saved dock position / width / pop-out bounds, theme, flushing views before disposal, owners for
    /// window.open popups, closing with the document. Configured on first use, not at startup.
    /// </summary>
    public static class TaskPaneManager
    {
        const string Section = "SidePane";
        static bool _configured;

        static void EnsureConfigured()
        {
            if (_configured) return;
            _configured = true;

            SidePanes.Log = s => Console.WriteLine("[SidePanes] " + s);
            SidePanes.Configure(Globals.ThisAddIn.CustomTaskPanes, ActiveWindowHandle);
            SidePanes.UseRibbon(() => new KleiKodesh.Ribbon.KeliKodeshRibbon());
            SidePanes.MenuTitle = rtl => AddinUiLanguage.T("חלוניות", "Panes");
            SidePanes.NewPaneText = rtl => KleiKodesh.Ribbon.AddinText.Label("NewPane");
            PaneMenu.Palette = () => OfficeThemeWatcher.MenuColors;
            SidePanes.NewPaneSuperTip = rtl => KleiKodesh.Ribbon.AddinText.Tip("NewPane");

            try
            {
                var icon = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KleiKodesh_Main.ico");
                if (File.Exists(icon)) SidePanes.PopOutIcon = new Icon(icon);
            }
            catch { }

            SidePanes.PaneCreated = OnPaneCreated;
            SidePanes.ViewCreated = OnViewCreated;
            SidePanes.ViewClosing = FlushView;

            SidePanes.LoadPaneWidth = () => { int w = SettingsManager.GetInt(Section, "Width", -1); return w > 0 ? w : (int?)null; };
            SidePanes.SavePaneWidth = w => SettingsManager.Save(Section, "Width", w);
            SidePanes.LoadPopOutBounds = () =>
            {
                int x = SettingsManager.GetInt(Section, "PopOutX", int.MinValue), y = SettingsManager.GetInt(Section, "PopOutY", int.MinValue);
                int w = SettingsManager.GetInt(Section, "PopOutW", 0), h = SettingsManager.GetInt(Section, "PopOutH", 0);
                return x == int.MinValue || w <= 0 || h <= 0 ? (Rectangle?)null : new Rectangle(x, y, w, h);
            };
            SidePanes.SavePopOutBounds = r =>
            {
                SettingsManager.Save(Section, "PopOutX", r.X); SettingsManager.Save(Section, "PopOutY", r.Y);
                SettingsManager.Save(Section, "PopOutW", r.Width); SettingsManager.Save(Section, "PopOutH", r.Height);
            };
        }

        /// <summary>
        /// Shows view <paramref name="id"/> (a ribbon pane id) in the current side pane of the active window,
        /// building it with <paramref name="factory"/> only if that side pane doesn't have it yet. Returns the view.
        /// </summary>
        public static T Show<T>(string id, Func<T> factory, string title) where T : Control
        {
            try
            {
                EnsureConfigured();
                return SidePanes.ShowView(id, title, () => factory()) as T;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error");
                return null;
            }
        }

        /// <summary>Opens another side pane in the active window (ribbon "open new side pane").</summary>
        public static void OpenNewPane() { EnsureConfigured(); SidePanes.OpenNewPane(); }

        /// <summary>Every live view of <paramref name="id"/>, the current side pane's first. Never creates one.</summary>
        public static List<T> FindAll<T>(string id) where T : Control =>
            _configured ? SidePanes.FindAllViews(id).OfType<T>().ToList() : new List<T>();

        /// <summary>The window handle of the window the user is in, or zero if there is none.</summary>
        public static int ActiveWindowHandle()
        {
            try { return Globals.ThisAddIn.Application.ActiveWindow.Hwnd; }
            catch { return 0; }
        }

        // ---------------- hooks ----------------

        static void OnPaneCreated(Microsoft.Office.Tools.CustomTaskPane pane)
        {
            RestoreDockPosition(pane);
            OfficeThemeWatcher.Attach(pane.Control);

            // The side pane closes with its document.
            try
            {
                int window = (pane.Window as Word.Window)?.Hwnd ?? 0;
                var doc = (pane.Window as Word.Window)?.Document;
                if (doc != null)
                    Globals.Factory.GetVstoObject(doc).CloseEvent += () => SidePanes.CloseAllFor(window);
            }
            catch { /* best effort: Office's X still closes it */ }
        }

        static void OnViewCreated(string id, Control view)
        {
            // Office theme colours: the watcher follows controls added to the root it is given, so each view is a root.
            OfficeThemeWatcher.Attach(view);

            // A view finishing its load makes Office repaint the pane: redraw our title controls then.
            RepaintTitleWhenLoaded(view);

            // window.open popups (e.g. Hameshiv) become owned windows of Word.
            var setPopupOwner = view.GetType().GetMethod("SetPopupOwnerAction");
            setPopupOwner?.Invoke(view, new object[] { new Action<IntPtr>(SetPopupOwner) });

            // Elevated Word next to a normal one: the view can offer to move this document to a normal Word.
            // The document of the window the view is created for.
            var setReopen = view.GetType().GetMethod("SetReopenUnelevatedAction");
            if (setReopen != null)
            {
                Word.Document doc = null;
                try { doc = Globals.ThisAddIn.Application.ActiveWindow.Document; } catch { }
                if (doc != null)
                    setReopen.Invoke(view, new object[] { new Func<bool>(() => ReopenUnelevated(doc)) });
            }
        }

        /// <summary>
        /// The page's pending persistence writes (tab list, recents, reading position) go out before its
        /// WebView2 is disposed - the view closes without any of the page's unload events. Bounded; never throws.
        /// </summary>
        static async Task FlushView(Control view)
        {
            if (view is KitveiHakodeshLib.AppViewer viewer && !viewer.IsDisposed)
                await viewer.FlushPersistenceAsync();
        }

        /// <summary>Makes a top-level window (a window.open popup) an owned window of Word's frame.</summary>
        static void SetPopupOwner(IntPtr formHandle)
        {
            // Popped out: the popup belongs above the pop-out window, not behind it above Word.
            var popOut = SidePanes.CurrentPopOut;
            IntPtr owner = popOut != null && popOut.IsHandleCreated ? popOut.Handle : (IntPtr)ActiveWindowHandle();
            if (owner == IntPtr.Zero) return;
            Native.SetWindowLongPtr(formHandle, Native.GWLP_HWNDPARENT, Native.GetAncestor(owner, Native.GA_ROOT));
        }

        static void RestoreDockPosition(Microsoft.Office.Tools.CustomTaskPane pane)
        {
            try
            {
                pane.DockPosition = SettingsManager.GetEnum(Section, "DockPosition", GetDefaultDockPosition());
                pane.DockPositionChanged += (s, e) => SettingsManager.Save(Section, "DockPosition", pane.DockPosition);
            }
            catch
            {
                pane.DockPosition = DockPosition.msoCTPDockPositionLeft;
            }
        }

        static DockPosition GetDefaultDockPosition()
        {
            int uiLang = Globals.ThisAddIn.Application
                .LanguageSettings
                .LanguageID[Microsoft.Office.Core.MsoAppLanguageID.msoLanguageIDUI];

            return (uiLang == 1037 || uiLang == 1025)
                ? DockPosition.msoCTPDockPositionLeft
                : DockPosition.msoCTPDockPositionRight;
        }

        /// <summary>
        /// When a view finishes loading (moves focus, swaps its content), Office repaints the side pane and draws over
        /// our title controls without telling us. Each kind of view says when it is done, and the title is redrawn:
        /// Kitvei Hakodesh when its splash goes, a WPF view after its first load (idle), a web view after its first page.
        /// </summary>
        static void RepaintTitleWhenLoaded(Control view)
        {
            if (view is KitveiHakodeshLib.AppViewer viewer)
            {
                viewer.SplashHidden += (s, e) => SidePanes.RepaintTitle(view);
                return;
            }
            var wpf = view.Controls.OfType<System.Windows.Forms.Integration.ElementHost>().FirstOrDefault()?.Child as System.Windows.FrameworkElement;
            if (wpf != null)
            {
                System.Windows.RoutedEventHandler loaded = null;
                loaded = (s, e) =>
                {
                    wpf.Loaded -= loaded;
                    // After the view's own deferred first load (queued at the same idle priority from its Loaded).
                    wpf.Dispatcher.BeginInvoke(new Action(() => SidePanes.RepaintTitle(view)), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                };
                wpf.Loaded += loaded;
                return;
            }
            var web = Descendants(view).OfType<Microsoft.Web.WebView2.WinForms.WebView2>().FirstOrDefault();
            if (web != null)
            {
                EventHandler<Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs> done = null;
                done = (s, e) => { web.NavigationCompleted -= done; SidePanes.RepaintTitle(view); };
                web.NavigationCompleted += done;
            }
        }

        static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control c in root.Controls) { yield return c; foreach (var d in Descendants(c)) yield return d; }
        }

        // ---------------- focus toggle (macro shortcut) ----------------

        /// <summary>
        /// Keyboard focus toggle for a macro shortcut: from the document into the current side pane's view
        /// (showing the side pane if needed), and from there back to the document. With no side pane, nothing.
        /// </summary>
        public static void ToggleFocus()
        {
            if (!_configured) return;
            var view = SidePanes.CurrentView;
            if (view == null) return;

            if (HasFocus(view))
            {
                var window = Globals.ThisAddIn.Application.ActiveWindow;
                window.Activate();   // leaves a popped-out window
                window.SetFocus();   // leaves a docked pane
                return;
            }

            SidePanes.RevealCurrent();
            FocusInto(view);
        }

        // Whether keyboard focus is inside the view. Read from the foreground thread: WebView2 content is a
        // window of another process, so GetFocus on this thread does not see it, but it is still a child window.
        static bool HasFocus(Control view)
        {
            var info = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
            if (!GetGUIThreadInfo(0, ref info) || info.hwndFocus == IntPtr.Zero) return false;
            return view.IsHandleCreated && (info.hwndFocus == view.Handle || IsChild(view.Handle, info.hwndFocus));
        }

        static void FocusInto(Control view)
        {
            if (view is KitveiHakodeshLib.AppViewer viewer) { viewer.FocusWebContent(); return; }

            var wpf = view.Controls.OfType<System.Windows.Forms.Integration.ElementHost>().FirstOrDefault();
            if (wpf?.Child != null)
            {
                wpf.Focus();
                wpf.Child.Focus();
                wpf.Child.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
                return;
            }
            (view.Controls.Count > 0 ? view.Controls[0] : view).Focus();
        }

        [StructLayout(LayoutKind.Sequential)]
        struct GUITHREADINFO
        {
            public int cbSize, flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public int left, top, right, bottom;
        }

        [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);
        [DllImport("user32.dll")] static extern bool IsChild(IntPtr parent, IntPtr child);

        // ---------------- reopen in a normal (unelevated) Word ----------------

        /// <summary>
        /// Closes <paramref name="doc"/> in this (elevated) Word and opens it again through
        /// Explorer, which runs without admin rights - so the file lands in a normal Word,
        /// where the pane can use the shared WebView2 data folder.
        ///
        /// Close goes first, with Word's own save prompt: launching first and then having
        /// the user cancel the prompt would leave the file open twice. Returns false when
        /// there is no file on disk yet. Quits this Word when the document was its last
        /// one - an empty admin Word only invites the next clash.
        ///
        /// The close / launch / quit run on a one-shot timer, after the pane's own call
        /// has returned: closing the document disposes the pane, and quitting tears down
        /// the add-in - neither may happen while the pane's code is still on the stack.
        /// A cancelled save prompt just leaves everything as it was, with no message.
        /// </summary>
        static bool ReopenUnelevated(Word.Document doc)
        {
            string path;
            try
            {
                path = doc.FullName;
                if (string.IsNullOrEmpty(doc.Path) || !System.IO.File.Exists(path)) return false;
            }
            catch { return false; }

            var timer = new Timer { Interval = 1 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                try
                {
                    var app = Globals.ThisAddIn.Application;
                    object prompt = Word.WdSaveOptions.wdPromptToSaveChanges;
                    doc.Close(ref prompt);   // throws if the user cancels the save prompt

                    System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"")?.Dispose();

                    if (app.Documents.Count == 0)
                    {
                        object noSave = Word.WdSaveOptions.wdDoNotSaveChanges;
                        app.Quit(ref noSave);
                    }
                }
                catch { /* cancelled at the save prompt: the document stays open here */ }
            };
            timer.Start();
            return true;
        }
    }
}
