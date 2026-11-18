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
            public bool Closing;
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
        static SidePane _target;                              // set while a menu entry opens its view: that side pane, whatever window is active

        /// <summary>The side pane the user is working in (focus entered it, its window was activated).</summary>
        internal static void MakeCurrent(SidePane side) { if (!side.Closing && All.Contains(side)) _current = side; }

        /// <summary>The live view with this id in the current side pane of the active window, or null.</summary>
        public static Control FindView(string id) => CurrentFor(Window(), create: false)?.Views.TryGetValue(id, out var v) == true ? v : null;

        /// <summary>All live views with this id, current side pane first.</summary>
        public static IEnumerable<Control> FindAllViews(string id) =>
            All.OrderByDescending(s => s == _current).Select(s => s.Views.TryGetValue(id, out var v) ? v : null).Where(v => v != null && !v.IsDisposed).ToList();

        /// <summary>
        /// Shows view <paramref name="id"/> in the current side pane of the active window: swaps it in if that side
        /// pane already has it (state kept), builds it with <paramref name="create"/> otherwise. Returns the view.
        /// </summary>
        public static Control ShowView(string id, string title, Func<Control> create)
        {
            var side = _target != null && !_target.Closing && !_target.Host.IsDisposed ? _target : CurrentFor(Window(), create: true);
            if (side == null) return null;
            Control created = null;
            if (!side.Views.TryGetValue(id, out var existing) || existing.IsDisposed)
                created = create();                               // its failure is the caller's to report
            try
            {
                var view = created ?? existing;
                if (created != null)
                {
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
            OpenNewPane(CurrentFor(Window(), create: false));
        }

        /// <summary>Another side pane next to <paramref name="from"/> (same document window), or in the active window.</summary>
        static void OpenNewPane(SidePane from)
        {
            var side = CreateSidePane(from?.Window ?? Window());
            if (side == null) return;
            _current = side;
            var entry = Ribbon?.Entries().FirstOrDefault(p => p.Id == from?.ActiveId) ?? Ribbon?.Entries().FirstOrDefault();
            if (entry != null) OpenInto(side, entry);     // the ribbon's own onAction -> ShowView into the new side pane
            else Reveal(side);
        }

        /// <summary>Runs a ribbon entry's onAction with its ShowView aimed at <paramref name="side"/>.</summary>
        static void OpenInto(SidePane side, RibbonPanes.Entry entry)
        {
            var before = _target;
            _target = side;
            try { entry.Open(); } finally { _target = before; }
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
            if (side.Closing) return;
            if (!side.IsPoppedOut) PopOut(side);
            if (side.IsPoppedOut) side.PopOut.EnterFullScreen();       // it may have failed to pop out
        }

        /// <summary>Closes every side pane of the given document window for good (e.g. its document is closing).</summary>
        public static void CloseAllFor(int window)
        {
            foreach (var side in All.Where(s => s.Window == window).ToList()) Close(side);
        }

        static void Activate(SidePane side, string id)
        {
            if (side.ActiveId == id && side.Views[id].Visible) return;
            // Full screen belongs to the view that asked for it: another view gets the normal window back.
            if (side.ActiveId != id && side.IsPoppedOut) side.PopOut.LeaveFullScreen();
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
            // One the user can see, before a hidden one.
            var side = All.LastOrDefault(s => s.Window == window && (s.IsPoppedOut || IsShown(s))) ?? All.LastOrDefault(s => s.Window == window);
            if (side != null) return _current = side;
            return create ? _current = CreateSidePane(window) : null;
        }

        static bool IsShown(SidePane side) { try { return side.Pane.Visible; } catch { return false; } }

        /// <summary>The pop-out's place to remember; not its full-screen size.</summary>
        static void RememberBounds(PopOutWindow win)
        {
            try { if (!win.IsFullScreen) SavePopOutBounds(win.WindowState == FormWindowState.Normal ? win.Bounds : win.RestoreBounds); } catch { }
        }

        static SidePane CreateSidePane(int window)
        {
            if (_panes == null) { Log("SidePanes.Configure() was not called"); return null; }
            var host = new HostControl();
            CustomTaskPane pane;
            try { pane = _panes.Add(host, " "); }                   // blank: the title is ours (the selector)
            catch (Exception ex) { Log("could not create a side pane: " + ex.Message); host.Dispose(); return null; }
            var side = new SidePane { Pane = pane, Host = host, Window = window };
            All.Add(side);
            try { PaneCreated(pane); } catch (Exception ex) { Log("PaneCreated: " + ex.Message); }
            try { pane.Width = LoadPaneWidth() ?? DefaultWidth; } catch { }   // once, before it is first shown
            int savedWidth = 0;
            host.SizeChanged += (s, e) =>                                     // the user's drag: remember it
            {
                try
                {
                    var dock = pane.DockPosition;
                    if (!pane.Visible || (dock != Microsoft.Office.Core.MsoCTPDockPosition.msoCTPDockPositionLeft && dock != Microsoft.Office.Core.MsoCTPDockPosition.msoCTPDockPositionRight)) return;
                    int w = pane.Width;
                    if (w >= 150 && w != savedWidth) SavePaneWidth(savedWidth = w);   // not every layout pass, not a collapsing pane
                }
                catch { }
            };
            host.Views.Enter += (s, e) => MakeCurrent(side);                  // focus moved into this side pane
            AttachStrip(side);
            Log("new side pane (" + All.Count(s => s.Window == window) + " in this window)");
            return side;
        }

        /// <summary>Closes a side pane for good: views flushed and disposed, pop-out closed, pane removed.</summary>
        static async void Close(SidePane side)
        {
            if (side.Closing) return;
            side.Closing = true;
            All.Remove(side);
            if (_current == side) _current = All.LastOrDefault(s => s.Window == side.Window);
            if (side.IsPoppedOut) { RememberBounds(side.PopOut); try { side.PopOut.Hide(); } catch { } }   // gone at once, not after the flush
            foreach (var v in side.Views.Values.ToList())
                try { if (!v.IsDisposed) await ViewClosing(v); } catch (Exception ex) { Log("ViewClosing: " + ex.Message); }
            if (side.IsPoppedOut)
            {
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
            if (side.Closing) return;
            var rows = new List<PaneMenu.Row>();
            foreach (var entry in Ribbon?.Entries() ?? new List<RibbonPanes.Entry>())
            {
                var e = entry;
                rows.Add(new PaneMenu.Row
                {
                    Text = e.Title, Image = e.Icon, Current = e.Id == side.ActiveId,
                    Click = () => { if (e.Id == side.ActiveId || side.Closing) return; _current = side; OpenInto(side, e); },
                });
            }
            rows.Add(PaneMenu.Row.Separator);
            rows.Add(new PaneMenu.Row { Text = NewPaneText(rtl), Glyph = Fluent.NewPane, Click = () => { if (!side.Closing) OpenNewPane(side); } });
            PaneMenu.Show(MenuTitle(rtl), rows, anchor, owner, titleBack, rtl, scale);
        }

        // ================= pop-out =================

        static void TogglePopOut(SidePane side) { if (side.Closing) return; if (side.IsPoppedOut) PopIn(side); else PopOut(side); }

        static void PopOut(SidePane side)
        {
            if (side.Closing || side.IsPoppedOut) return;
            PopOutWindow win = null;
            var panel = side.Host.Views;
            try
            {
                win = new PopOutWindow(side);
                side.PopOut = win;
                // Owned by Word's frame (as the old pop-out). The pane's own top-level window first, read while the
                // pane is still shown: the document window handle can name an inner window, whose ownership doesn't
                // keep the pop-out above the frame.
                var frame = side.Host.IsHandleCreated ? Native.GetAncestor(side.Host.Handle, Native.GA_ROOT) : IntPtr.Zero;
                side.Host.Controls.Remove(panel);
                win.Content.Controls.Add(panel);
                Place(win, side);
                try { PopOutCreated(win); } catch (Exception ex) { Log("PopOutCreated: " + ex.Message); }
                try { side.Pane.Visible = false; } catch { }            // code hide: not the X
                win.SetOwner(frame != IntPtr.Zero && frame != side.Host.Handle ? frame : (IntPtr)side.Window);   // right before showing
                win.Show();
                win.Activate();                                       // hiding the pane handed activation back to Word
                Log("popped out " + side.ActiveId);
            }
            catch (Exception ex)
            {
                // Never leave the views stranded in a window that isn't there: back into the pane.
                Log("pop-out failed: " + ex.Message);
                side.PopOut = null;
                try { if (panel.Parent != side.Host) { panel.Parent?.Controls.Remove(panel); side.Host.Controls.Add(panel); } } catch { }
                try { if (win != null) { win.AllowClose = true; win.Dispose(); } } catch { }
                try { side.Pane.Visible = true; } catch { }
            }
        }

        internal static void PopIn(SidePane side)
        {
            var win = side.PopOut;
            if (win == null || win.IsDisposed || side.Closing) return;
            side.PopOut = null;
            RememberBounds(win);
            var panel = side.Host.Views;
            win.Content.Controls.Remove(panel);
            if (!side.Host.IsDisposed) side.Host.Controls.Add(panel);
            win.AllowClose = true;
            win.Close();
            win.Dispose();
            if (!side.Host.IsDisposed && side.Host.IsHandleCreated)
                side.Host.BeginInvoke((Action)(() => { try { if (!side.Closing) side.Pane.Visible = true; } catch { } }));
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
                        if (side.Closing) return;
                        if (side.Pane.Visible)
                        {
                            _current = side;
                            if (side.IsPoppedOut) { side.Host.BeginInvoke((Action)(() => PopIn(side))); return; }   // reopened while popped out
                            side.Host.BeginInvoke((Action)strip.Reattach);                                          // Office built a new frame
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
