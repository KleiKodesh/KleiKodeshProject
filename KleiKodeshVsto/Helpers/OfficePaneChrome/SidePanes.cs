using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Office.Tools;

namespace OfficePaneChrome
{
    /// <summary>
    /// Shared side panes for a VSTO add-in: views shuffle in and out of one task pane instead of each opening its own.
    ///
    ///   - A side pane hosts views (one per pane id); only the active one is visible, the others stay alive.
    ///     <see cref="ShowView"/> brings a view into the current side pane of the active document window,
    ///     creating the side pane if there is none. Call it from the ribbon's onAction for a pane id.
    ///   - Office's title is blank; its title strip shows our selector (current view's icon, name, chevron)
    ///     and a pop-out button. The selector's menu lists the ribbon's tag="pane" buttons
    ///     (<see cref="RibbonPanes"/>) plus "open new side pane" (<see cref="OpenNewPane"/>).
    ///   - Office's X closes the side pane for real: views flushed (<see cref="ViewClosing"/>), then disposed.
    ///   - Pop-out moves the side pane's content into a <see cref="ChromeWindow"/> with the same selector
    ///     and a pop-in button; showing a view while popped out uses that window.
    ///
    /// Setup (once):
    ///   SidePanes.Configure(Globals.ThisAddIn.CustomTaskPanes, () => Application.ActiveWindow.Hwnd);
    ///   SidePanes.UseRibbon(() => new MyRibbon());
    /// Ribbon onAction for a pane id:
    ///   SidePanes.ShowView(id, label, () => new MyView());
    ///
    /// If Office's title strip isn't what we know, the strip hook removes itself: panes, ShowView and the
    /// ribbon keep working, only the in-pane selector is missing.
    /// </summary>
    public static class SidePanes
    {
        // ================= configuration =================

        public static Action<string> Log = s => Debug.WriteLine("[SidePanes] " + s);
        public static Func<bool, string> MenuTitle = rtl => rtl ? "חלוניות" : "Panes";
        public static Func<bool, string> NewPaneText = rtl => rtl ? "פתח חלונית צד חדשה" : "Open new side pane";

        public static Func<bool, string> PopInTip = rtl => rtl ? "\u05D4\u05D7\u05D6\u05E8 \u05DC\u05D7\u05DC\u05D5\u05E0\u05D9\u05EA \u05D4\u05E6\u05D3" : "Back to the side pane";
        /// <summary>The pop-out window's icon (Alt+Tab, system menu).</summary>
        public static Icon PopOutIcon;

        /// <summary>Called once for each new side pane (dock position, theme, ...).</summary>
        public static Action<CustomTaskPane> PaneCreated = _ => { };
        /// <summary>Called once for each new view, after it is created and before it is shown (theme, owners, ...).</summary>
        public static Action<string, Control> ViewCreated = (_, __) => { };
        /// <summary>Called for each view before its side pane closes and it is disposed (flush state).</summary>
        public static Func<Control, Task> ViewClosing = _ => Task.CompletedTask;
        /// <summary>Called for each pop-out window once created (extra theming etc.).</summary>
        public static Action<Form> PopOutCreated = _ => { };
        /// <summary>
        /// One width for the side pane, whatever view it shows: the remembered one, else <see cref="DefaultWidth"/>.
        /// Applied only when a side pane is created - setting CustomTaskPane.Width later makes Office destroy and
        /// rebuild the pane (a visible hide/re-show, measured), while the user's own drag resizes it smoothly.
        /// </summary>
        public static int DefaultWidth = 510;
        public static Func<int?> LoadPaneWidth = () => _width;
        public static Action<int> SavePaneWidth = w => _width = w;
        /// <summary>The pop-out window's remembered screen bounds; default in memory.</summary>
        public static Func<Rectangle?> LoadPopOutBounds = () => _popOutBounds;
        public static Action<Rectangle> SavePopOutBounds = r => _popOutBounds = r;

        static int? _width;
        static Rectangle? _popOutBounds;
        static CustomTaskPaneCollection _panes;
        static Func<int> _activeWindow;
        internal static RibbonPanes Ribbon;

        public static void Configure(CustomTaskPaneCollection panes, Func<int> activeWindowHwnd)
        {
            _panes = panes;
            _activeWindow = activeWindowHwnd;
        }

        /// <summary>Where the menu's entries come from: a ribbon object (IRibbonExtensibility + its callbacks).</summary>
        public static void UseRibbon(Func<object> createRibbon) => Ribbon = new RibbonPanes(createRibbon);

        // ================= side panes and views =================

        internal sealed class SidePane
        {
            public CustomTaskPane Pane;
            public HostControl Host;
            public int Window;
            public string ActiveId;
            public readonly Dictionary<string, Control> Views = new Dictionary<string, Control>();
            public readonly Dictionary<string, string> Titles = new Dictionary<string, string>();
            public OfficeTitleStrip Strip;
            public PopOutWindow PopOut;
            public bool IsPoppedOut => PopOut != null && !PopOut.IsDisposed;
            public string ActiveTitle => ActiveId != null && Titles.TryGetValue(ActiveId, out var t) ? t : ActiveId ?? "";
            public Image ActiveIcon => Ribbon?.IconFor(ActiveId);
        }

        /// <summary>The pane's control: a container whose single child (the view panel) moves on pop-out.</summary>
        internal sealed class HostControl : UserControl
        {
            public readonly Panel Views = new Panel { Dock = DockStyle.Fill };
            public HostControl() { Controls.Add(Views); }
        }

        static readonly List<SidePane> All = new List<SidePane>();
        static SidePane _current;

        /// <summary>The live view with this id in the current side pane of the active window, or null.</summary>
        public static Control FindView(string id) => CurrentFor(Window(), create: false)?.Views.TryGetValue(id, out var v) == true ? v : null;

        /// <summary>All live views with this id, current side pane first.</summary>
        public static IEnumerable<Control> FindAllViews(string id) =>
            All.OrderByDescending(s => s == _current).Select(s => s.Views.TryGetValue(id, out var v) ? v : null).Where(v => v != null && !v.IsDisposed);

        /// <summary>
        /// Shows view <paramref name="id"/> in the current side pane of the active window: swaps it in if that side
        /// pane already has it (state kept), builds it with <paramref name="create"/> otherwise. Returns the view.
        /// </summary>
        public static Control ShowView(string id, string title, Func<Control> create)
        {
            var side = CurrentFor(Window(), create: true);
            if (side == null) return null;
            try
            {
                if (!side.Views.TryGetValue(id, out var view) || view.IsDisposed)
                {
                    view = create();
                    try { ViewCreated(id, view); } catch (Exception ex) { Log("ViewCreated: " + ex.Message); }
                    view.Dock = DockStyle.Fill;
                    view.Visible = false;
                    side.Views[id] = view;
                    side.Host.Views.Controls.Add(view);
                }
                side.Titles[id] = title;
                Activate(side, id);
                Reveal(side);
                return view;
            }
            catch (Exception ex) { Log("ShowView " + id + ": " + ex.Message); return null; }
        }

        /// <summary>Opens another side pane in the active window, showing the current view there too.</summary>
        public static void OpenNewPane()
        {
            var from = CurrentFor(Window(), create: false);
            var side = CreateSidePane(Window());
            if (side == null) return;
            _current = side;
            var entry = Ribbon?.Entries().FirstOrDefault(p => p.Id == from?.ActiveId) ?? Ribbon?.Entries().FirstOrDefault();
            if (entry != null) entry.Open();              // the ribbon's own onAction -> ShowView into the new side pane
            else Reveal(side);
        }

        /// <summary>Pops the current side pane of the active window out into its own window, or back in.</summary>
        public static void TogglePopOut() { var side = CurrentFor(Window(), create: false); if (side != null) TogglePopOut(side); }

        /// <summary>The view shown in the current side pane of the active window, or null.</summary>
        public static Control CurrentView
        {
            get { var side = CurrentFor(Window(), create: false); return side?.ActiveId != null && side.Views.TryGetValue(side.ActiveId, out var v) ? v : null; }
        }

        /// <summary>The current side pane's pop-out window, or null when it is in the pane.</summary>
        public static Form CurrentPopOut { get { var side = CurrentFor(Window(), create: false); return side != null && side.IsPoppedOut ? side.PopOut : null; } }

        /// <summary>Shows the current side pane (pane or pop-out window); false if the active window has none.</summary>
        public static bool RevealCurrent() { var side = CurrentFor(Window(), create: false); if (side == null) return false; Reveal(side); return true; }

        /// <summary>
        /// Pops out the side pane holding <paramref name="view"/> (if it isn't already) and makes its window full
        /// screen - that side pane, not merely the current one, when a window has several.
        /// </summary>
        public static void PopOutFullScreen(Control view)
        {
            var side = All.FirstOrDefault(s => s.Views.ContainsValue(view)) ?? CurrentFor(Window(), create: false);
            if (side == null) return;
            if (!side.IsPoppedOut) PopOut(side);
            side.PopOut.FormBorderStyle = FormBorderStyle.None;      // ChromeWindow hides its title bar
            side.PopOut.WindowState = FormWindowState.Maximized;
        }

        /// <summary>Closes every side pane of the given document window for good (e.g. its document is closing).</summary>
        public static void CloseAllFor(int window)
        {
            foreach (var side in All.Where(s => s.Window == window).ToList()) Close(side);
        }

        static void Activate(SidePane side, string id)
        {
            if (side.ActiveId == id && side.Views[id].Visible) return;
            side.ActiveId = id;
            side.Strip?.Repaint();                                     // the title first: the view swap can be heavy
            side.PopOut?.RefreshTitle();
            foreach (var kv in side.Views) kv.Value.Visible = kv.Key == id;
            Log("side pane shows " + id);
        }

        static void Reveal(SidePane side)
        {
            _current = side;
            if (side.IsPoppedOut)
            {
                // Popped out: bring the window forward instead of showing an empty pane.
                if (side.PopOut.WindowState == FormWindowState.Minimized) side.PopOut.WindowState = FormWindowState.Normal;
                side.PopOut.Activate();
                return;
            }
            try { side.Pane.Visible = true; } catch { }
        }

        static int Window() { try { return _activeWindow?.Invoke() ?? 0; } catch { return 0; } }

        static SidePane CurrentFor(int window, bool create)
        {
            All.RemoveAll(s => s.Host.IsDisposed);
            if (_current != null && _current.Window == window && !_current.Host.IsDisposed) return _current;
            var side = All.LastOrDefault(s => s.Window == window);
            if (side != null) return _current = side;
            return create ? _current = CreateSidePane(window) : null;
        }

        static SidePane CreateSidePane(int window)
        {
            if (_panes == null) { Log("SidePanes.Configure() was not called"); return null; }
            var host = new HostControl();
            var pane = _panes.Add(host, " ");                       // blank: the title is ours (the selector)
            var side = new SidePane { Pane = pane, Host = host, Window = window };
            All.Add(side);
            try { PaneCreated(pane); } catch (Exception ex) { Log("PaneCreated: " + ex.Message); }
            try { pane.Width = LoadPaneWidth() ?? DefaultWidth; } catch { }   // once, before it is first shown
            host.SizeChanged += (s, e) =>                                     // the user's drag: remember it
            {
                try
                {
                    var dock = pane.DockPosition;
                    if (pane.Visible && (dock == Microsoft.Office.Core.MsoCTPDockPosition.msoCTPDockPositionLeft || dock == Microsoft.Office.Core.MsoCTPDockPosition.msoCTPDockPositionRight))
                        SavePaneWidth(pane.Width);
                }
                catch { }
            };
            AttachStrip(side);
            Log("new side pane (" + All.Count(s => s.Window == window) + " in this window)");
            return side;
        }

        /// <summary>Closes a side pane for good: views flushed and disposed, pop-out closed, pane removed.</summary>
        static async void Close(SidePane side)
        {
            All.Remove(side);
            if (_current == side) _current = All.LastOrDefault(s => s.Window == side.Window);
            foreach (var v in side.Views.Values.ToList())
                try { if (!v.IsDisposed) await ViewClosing(v); } catch (Exception ex) { Log("ViewClosing: " + ex.Message); }
            if (side.IsPoppedOut)
            {
                try { SavePopOutBounds(side.PopOut.WindowState == FormWindowState.Normal ? side.PopOut.Bounds : side.PopOut.RestoreBounds); } catch { }
                side.PopOut.AllowClose = true;
                try { side.PopOut.Dispose(); } catch { }
            }
            side.Strip?.Dispose();
            try { _panes.Remove(side.Pane); } catch { }
            try { side.Host.Dispose(); } catch { }
            Log("side pane closed; " + All.Count + " left");
        }

        // ================= the selector's menu =================

        internal static void ShowMenu(SidePane side, Rectangle anchor, IntPtr owner, Color titleBack, bool rtl, float scale)
        {
            var rows = new List<PaneMenu.Row>();
            foreach (var entry in Ribbon?.Entries() ?? new List<RibbonPanes.Entry>())
            {
                var e = entry;
                rows.Add(new PaneMenu.Row
                {
                    Text = e.Title, Image = e.Icon, Current = e.Id == side.ActiveId,
                    Click = () => { if (e.Id == side.ActiveId) return; _current = side; e.Open(); },
                });
            }
            rows.Add(PaneMenu.Row.Separator);
            rows.Add(new PaneMenu.Row { Text = NewPaneText(rtl), Glyph = Fluent.NewPane, Click = () => { _current = side; OpenNewPane(); } });
            PaneMenu.Show(MenuTitle(rtl), rows, anchor, owner, titleBack, rtl, scale);
        }

        // ================= pop-out =================

        static void TogglePopOut(SidePane side) { if (side.IsPoppedOut) PopIn(side); else PopOut(side); }

        static void PopOut(SidePane side)
        {
            var win = new PopOutWindow(side);
            side.PopOut = win;
            var panel = side.Host.Views;
            side.Host.Controls.Remove(panel);
            win.Content.Controls.Add(panel);
            Place(win, side);
            try { PopOutCreated(win); } catch (Exception ex) { Log("PopOutCreated: " + ex.Message); }
            try { side.Pane.Visible = false; } catch { }            // code hide: not the X
            // Owned by Word's frame, set right before showing (as the old pop-out). The pane's own top-level
            // window first: the document window handle can name an inner window, whose ownership doesn't keep
            // the pop-out above the frame.
            var frame = side.Host.IsHandleCreated ? Native.GetAncestor(side.Host.Handle, Native.GA_ROOT) : IntPtr.Zero;
            win.SetOwner(frame != IntPtr.Zero && frame != side.Host.Handle ? frame : (IntPtr)side.Window);
            win.Show();
            win.Activate();                                       // hiding the pane handed activation back to Word
            Log("popped out " + side.ActiveId);
        }

        internal static void PopIn(SidePane side)
        {
            var win = side.PopOut;
            if (win == null || win.IsDisposed) return;
            side.PopOut = null;
            try { SavePopOutBounds(win.WindowState == FormWindowState.Normal ? win.Bounds : win.RestoreBounds); } catch { }
            var panel = side.Host.Views;
            win.Content.Controls.Remove(panel);
            if (!side.Host.IsDisposed) side.Host.Controls.Add(panel);
            win.AllowClose = true;
            win.Close();
            win.Dispose();
            if (!side.Host.IsDisposed && side.Host.IsHandleCreated)
                side.Host.BeginInvoke((Action)(() => { try { side.Pane.Visible = true; } catch { } }));
            else try { side.Pane.Visible = true; } catch { }
            Log("popped in " + side.ActiveId);
        }

        /// <summary>Remembered bounds if still on a screen, else centred on Word.</summary>
        static void Place(Form win, SidePane side)
        {
            var saved = LoadPopOutBounds();
            if (saved.HasValue && Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(saved.Value.X, saved.Value.Y, saved.Value.Width, 32))))
            { win.Bounds = saved.Value; return; }
            var area = Screen.FromHandle((IntPtr)side.Window).WorkingArea;
            int w = Math.Max(480, side.Pane.Width), h = Math.Min(850, area.Height - 40);
            win.Bounds = new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
        }

        // ================= title strip wiring =================

        static void AttachStrip(SidePane side)
        {
            try
            {
                var strip = new OfficeTitleStrip(side.Host);
                side.Strip = strip;
                strip.Current = () => (side.ActiveTitle, side.ActiveIcon);
                strip.SelectorClicked += () => ShowMenu(side, strip.SelectorScreenRect(), strip.OwnerWindow, strip.Background, strip.RightToLeft, strip.Scale);
                strip.PopOutClicked += () => TogglePopOut(side);

                EventHandler onVisible = null;
                onVisible = (s, e) =>
                {
                    try
                    {
                        if (side.Pane.Visible)
                        {
                            _current = side;
                            if (side.IsPoppedOut) { side.Host.BeginInvoke((Action)(() => PopIn(side))); return; }   // reopened while popped out
                            side.Host.BeginInvoke((Action)strip.Attach);                                          // Office built a new frame
                            return;
                        }
                        if (!strip.InOfficeClose) return;                    // not the X: stock hide
                        Log("X: closing side pane");
                        side.Host.BeginInvoke((Action)(() => { side.Pane.VisibleChanged -= onVisible; Close(side); }));
                    }
                    catch (Exception ex) { Log("VisibleChanged: " + ex.Message); }
                };
                side.Pane.VisibleChanged += onVisible;
                side.Host.Disposed += (s, e) => strip.Dispose();
            }
            catch (Exception ex) { Log("title strip attach failed: " + ex.Message); }
        }
    }
}
