using KleiKodesh.Helpers;
using KleiKodesh.Ribbon;
using Microsoft.Office.Tools;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DockPosition = Microsoft.Office.Core.MsoCTPDockPosition;

namespace KleiKodesh.ToolRail
{
    /// <summary>
    /// The control inside the one "Klei Kodesh" task pane of a Word window: the tools
    /// the user has opened, and - once there are two or more of them - a narrow icon
    /// rail on the pane's outer edge to switch between them. With a single tool open
    /// there is no rail and the pane is that tool alone.
    ///
    /// Each tool is built the first time it is opened and then kept alive; switching
    /// only changes which one is Visible, inside a suspended layout. Rebuilding on every
    /// switch is not an option: AppViewer and KiwixWebview cache their WebView2
    /// environment in a static, and a tool would also lose its tabs, scroll position
    /// and search. Measured outside Word (2026-11-16, real AppViewer): hiding and
    /// showing it again cost 6-14 ms for the show and 13-36 ms to the first script
    /// answer, and its WebView2 did not report itself suspended - so a switch is cheap.
    ///
    /// The pane keeps ONE width for all tools. Each tool used to apply its own width
    /// (610 / 600 / 520 / 400), which moved the document's edge on every switch - the
    /// "jumps" users saw.
    /// </summary>
    public sealed class ToolRailHost : UserControl
    {
        const int RailLogicalWidth = 50, ItemLogical = 40, PitchLogical = 46, PadLogical = 6;

        sealed class Slot
        {
            public ToolEntry Entry;
            public Control Control;
            public TaskPanePopOut PopOut;
            public bool IsPoppedOut => PopOut != null && PopOut.IsPoppedOut;
        }

        readonly Panel _rail = new RailPanel { Visible = false };
        readonly Panel _content = new Panel { Dock = DockStyle.Fill };
        readonly Panel _placeholder = new Panel { Dock = DockStyle.Fill, Visible = false };
        readonly Label _placeholderText = new Label { Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleCenter };
        readonly Button _placeholderReturn = new Button { Dock = DockStyle.Top, AutoSize = true };
        readonly ToolTip _tip = new ToolTip { ShowAlways = true };
        readonly ContextMenuStrip _more = new ContextMenuStrip();
        readonly ContextMenuStrip _itemMenu = new ContextMenuStrip();
        readonly RailButton _moreButton = new RailButton { Glyph = "⋯", Visible = false };

        // Open tools, in the order they were opened - the rail lists them in this order.
        readonly List<Slot> _slots = new List<Slot>();
        readonly List<RailButton> _buttons = new List<RailButton>();
        readonly List<RailButton> _overflow = new List<RailButton>();

        string _buttonsSignature;
        CustomTaskPane _pane;
        Slot _active;
        bool _settingWidth, _widthApplied;

        public ToolRailHost()
        {
            _rail.Dock = DockStyle.Left;
            _rail.Layout += (_, __) => LayoutRail();

            _placeholderText.Padding = new Padding(12, 24, 12, 12);
            _placeholderReturn.Click += (_, __) => PopInActive();
            _placeholder.Controls.Add(_placeholderReturn);
            _placeholder.Controls.Add(_placeholderText);
            _content.Controls.Add(_placeholder);

            _moreButton.Click += (_, __) => ShowOverflowMenu();
            _rail.Controls.Add(_moreButton);

            // Docking runs from the back of the z-order: the rail (added last) takes
            // its edge first and the content fills what is left.
            Controls.Add(_content);
            Controls.Add(_rail);
        }

        internal CustomTaskPane Pane => _pane;

        int L(int logical) => LogicalToDeviceUnits(logical);

        int RailWidth => L(RailLogicalWidth);

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (_rail.Width != RailWidth) _rail.Width = RailWidth;
            base.OnLayout(e);
        }

        // ── Pane ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Binds this host to the pane that holds it: dock position from settings
        /// (Hebrew and Arabic Word dock left, as every pane does), and the rail kept
        /// on the pane's outer edge as the pane moves.
        /// </summary>
        internal void Attach(CustomTaskPane pane)
        {
            _pane = pane;
            try
            {
                pane.DockPosition = SettingsManager.GetEnum(
                    RailPane.Section, "DockPosition", TaskPaneManager.GetDefaultDockPosition());
            }
            catch { /* a dock position Word refuses keeps Word's default */ }

            pane.DockPositionChanged += (_, __) =>
            {
                try { SettingsManager.Save(RailPane.Section, "DockPosition", pane.DockPosition); } catch { }
                ApplyRailSide();
            };
            ApplyRailSide();
        }

        DockPosition Dock_
        {
            get
            {
                try { return _pane?.DockPosition ?? DockPosition.msoCTPDockPositionLeft; }
                catch { return DockPosition.msoCTPDockPositionLeft; }
            }
        }

        bool SideDocked =>
            Dock_ == DockPosition.msoCTPDockPositionLeft || Dock_ == DockPosition.msoCTPDockPositionRight;

        internal bool DockedLeft => Dock_ != DockPosition.msoCTPDockPositionRight;

        // The outer edge is the one away from the document.
        void ApplyRailSide()
        {
            var side = Dock_ == DockPosition.msoCTPDockPositionRight ? DockStyle.Right : DockStyle.Left;
            if (_rail.Dock != side)
            {
                _rail.Dock = side;
                _rail.Invalidate();
            }
        }

        void RevealPane() => TaskPaneManager.Reveal(_pane);

        // ── Width: one for the whole pane ─────────────────────────────────────

        // Applied once, when the pane first shows: the saved width, or the first tool's
        // default. After that only the user changes it, by dragging the pane's edge.
        void ApplyWidthOnce(ToolEntry first)
        {
            if (_widthApplied || !SideDocked) return;
            _widthApplied = true;
            int width = SettingsManager.GetInt(RailPane.Section, "PaneWidth", first.DefaultWidth);
            _settingWidth = true;
            try { _pane.Width = width; }
            catch { /* Word refuses a width when the pane is floating or docked top/bottom */ }
            finally { _settingWidth = false; }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            SaveWidth();
        }

        void SaveWidth()
        {
            if (_settingWidth || !_widthApplied || _pane == null) return;
            try
            {
                if (!_pane.Visible || !SideDocked) return;
                int width = _pane.Width;
                // A width this narrow is Word laying the pane out, not a user choice.
                if (width < L(150)) return;
                SettingsManager.Save(RailPane.Section, "PaneWidth", width);
            }
            catch { }
        }

        // ── Showing tools ─────────────────────────────────────────────────────

        Slot Find(string id) => _slots.FirstOrDefault(s => s.Entry.Id == id && !s.Control.IsDisposed);

        /// <summary>
        /// Shows <paramref name="entry"/> and returns its live control - the one a
        /// context-menu search drives. A tool that is in a floating window is brought
        /// forward there instead. Program and macro items run and return null.
        /// </summary>
        internal Control ShowTool(ToolEntry entry)
        {
            if (entry.Kind == ToolKind.Program) { ExternalTools.RunProgram(entry, DockedLeft); return null; }
            if (entry.Kind == ToolKind.Macro) { ExternalTools.RunMacro(entry); return null; }

            var slot = Find(entry.Id);
            if (slot == null)
                slot = Build(entry);
            if (slot == null) return null;

            if (slot.IsPoppedOut)
            {
                slot.PopOut.FocusPopOutWindow();
                UpdateButtonStates();
                return slot.Control;
            }

            RevealPane();
            ApplyWidthOnce(entry);
            Activate(slot);
            return slot.Control;
        }

        void Activate(Slot slot)
        {
            // One layout pass for the whole swap, not one per Visible change.
            SuspendLayout();
            _content.SuspendLayout();
            try
            {
                _placeholder.Visible = false;
                slot.Control.Visible = true;
                slot.Control.BringToFront();
                foreach (var other in _slots)
                    if (other != slot && !other.Control.IsDisposed && other.Control.Visible)
                        other.Control.Visible = false;
                _active = slot;
                RefreshButtons();
            }
            finally
            {
                _content.ResumeLayout(true);
                ResumeLayout(true);
            }
        }

        Slot Build(ToolEntry entry)
        {
            Control control;
            System.Windows.Controls.UserControl wpfView;
            control = entry.BuildSlotControl(out wpfView);
            if (control == null) return null;

            var slot = new Slot { Entry = entry, Control = control };
            control.Dock = DockStyle.Fill;
            _content.Controls.Add(control);

            // The theme watcher follows controls added to a root it knows, but only
            // directly under that root - not under the content panel. Attach each
            // tool as its own root, as each tool's own pane did before.
            OfficeThemeWatcher.Attach(control);

            if (control is UserControl host)
            {
                // Pop-out takes this tool alone into a floating window; the pane and
                // the other tools stay where they are.
                slot.PopOut = new TaskPanePopOut(host, () => OnSlotPoppedOut(slot), () => OnSlotPoppedIn(slot));

                var setPopOut = control.GetType().GetMethod("SetPopOutToggleAction");
                setPopOut?.Invoke(control, new object[] { new Action<bool>(slot.PopOut.Toggle) });
                if (wpfView != null)
                    WpfTaskPane.ForwardPopOutToggle(wpfView, slot.PopOut);
            }

            // window.open popups (e.g. Hameshiv) become owned windows of Word.
            var setPopupOwner = control.GetType().GetMethod("SetPopupOwnerAction");
            setPopupOwner?.Invoke(control, new object[] { new Action<IntPtr>(TaskPanePopOut.SetOwner) });

            _slots.Add(slot);
            return slot;
        }

        void OnItemClick(ToolEntry entry)
        {
            try
            {
                AddinUiLanguage.Apply();
                ShowTool(entry);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, AddinText.Label("KleiKodesh_Main"));
            }
        }

        /// <summary>
        /// Closes one open tool: it leaves the rail, and with one tool left the rail
        /// goes too. Kitvei Hakodesh writes out its pending state first, as on document
        /// close. Closing the last tool hides the pane.
        /// </summary>
        async void CloseTool(Slot slot)
        {
            try
            {
                if (slot.Control is KitveiHakodeshLib.AppViewer viewer && !viewer.IsDisposed)
                    await viewer.FlushPersistenceAsync();
                slot.PopOut?.ClosePopOut();

                int index = _slots.IndexOf(slot);
                _slots.Remove(slot);
                _content.Controls.Remove(slot.Control);
                slot.Control.Dispose();

                if (_active == slot)
                {
                    _active = null;
                    // The tool that took the closed one's place on the rail, else the last.
                    var next = _slots.Count == 0 ? null : _slots[Math.Min(index, _slots.Count - 1)];
                    if (next != null)
                        ShowTool(next.Entry);
                    else
                    {
                        _placeholder.Visible = false;
                        try { _pane.Visible = false; } catch { }
                    }
                }
                RefreshButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, AddinText.Label("KleiKodesh_Main"));
            }
        }

        // ── Pop-out ───────────────────────────────────────────────────────────

        void OnSlotPoppedOut(Slot slot)
        {
            // TaskPanePopOut makes the moved content visible again after this.
            slot.Control.Visible = false;
            if (_active == slot)
            {
                _placeholderText.Text = AddinUiLanguage.T(
                    $"{slot.Entry.Label()} פתוח בחלון נפרד.",
                    $"{slot.Entry.Label()} is open in a separate window.");
                _placeholderReturn.Text = AddinUiLanguage.T("החזר לחלונית", "Return to the pane");
                _placeholderText.Height = L(80);
                _placeholder.Visible = true;
                _placeholder.BringToFront();
            }
            UpdateButtonStates();
        }

        void OnSlotPoppedIn(Slot slot)
        {
            if (IsDisposed || slot.Control.IsDisposed) return;
            ShowTool(slot.Entry);
        }

        void PopInActive()
        {
            if (_active != null && _active.IsPoppedOut)
                _active.PopOut.Toggle(false);
        }

        // ── Rail ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the icons when the set of open tools changed, shows the rail only
        /// while two or more are open, and refreshes which icon is marked.
        /// </summary>
        internal void RefreshButtons()
        {
            var open = _slots.Where(s => !s.Control.IsDisposed).ToList();
            string signature = string.Join("|", open.Select(s => s.Entry.Id + "/" + s.Entry.Label()));

            if (signature != _buttonsSignature)
            {
                _buttonsSignature = signature;

                foreach (var b in _buttons)
                {
                    _rail.Controls.Remove(b);
                    b.Dispose();
                }
                _buttons.Clear();

                foreach (var slot in open)
                {
                    var s = slot;
                    var b = new RailButton
                    {
                        Entry = s.Entry,
                        Icon = s.Entry.LoadIcon?.Invoke(),
                        AccessibleName = s.Entry.Label(),
                    };
                    b.Click += (_, __) => OnItemClick(s.Entry);
                    b.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowItemMenu(b, s, e.Location); };
                    _tip.SetToolTip(b, TipText(s.Entry));
                    _buttons.Add(b);
                    _rail.Controls.Add(b);
                }

                _tip.SetToolTip(_moreButton, AddinUiLanguage.T("כלים פתוחים נוספים", "More open tools"));
                _moreButton.AccessibleName = AddinUiLanguage.T("כלים פתוחים נוספים", "More open tools");

                bool showRail = open.Count >= 2;
                if (_rail.Visible != showRail) _rail.Visible = showRail;
                _rail.PerformLayout();
            }

            UpdateButtonStates();
        }

        static string TipText(ToolEntry e)
        {
            string label = e.Label();
            string tip = e.Tip?.Invoke();
            return string.IsNullOrWhiteSpace(tip) ? label : label + "\n" + tip;
        }

        void UpdateButtonStates()
        {
            foreach (var b in _buttons)
            {
                var slot = Find(b.Entry.Id);
                b.Active = _active != null && _active.Entry.Id == b.Entry.Id;
                b.PoppedOut = slot != null && slot.IsPoppedOut;
            }
            _moreButton.Active = _overflow.Any(b => b.Active);
        }

        void ShowItemMenu(RailButton button, Slot slot, Point at)
        {
            _itemMenu.Items.Clear();
            _itemMenu.RightToLeft = AddinUiLanguage.IsEnglish ? RightToLeft.No : RightToLeft.Yes;
            var close = new ToolStripMenuItem(AddinUiLanguage.T("סגור את " + slot.Entry.Label(), "Close " + slot.Entry.Label()));
            close.Click += (_, __) => CloseTool(slot);
            _itemMenu.Items.Add(close);
            _itemMenu.Show(button, at);
        }

        // Icons from the top, in the order the tools were opened. What does not fit in
        // the height the user can actually see goes behind a "more" button - nothing is
        // anchored to the bottom edge, whose position Word did not report reliably
        // (a Settings icon placed there never appeared).
        void LayoutRail()
        {
            int item = L(ItemLogical), pitch = L(PitchLogical), pad = L(PadLogical);
            int x = (_rail.Width - item) / 2;
            int height = VisibleHeight(_rail);

            int fit = Math.Max(1, (height - pad) / pitch);
            bool overflow = _buttons.Count > fit;
            int shown = overflow ? Math.Max(1, fit - 1) : _buttons.Count;

            _overflow.Clear();
            for (int i = 0; i < _buttons.Count; i++)
            {
                if (i < shown)
                {
                    _buttons[i].SetBounds(x, pad + i * pitch, item, item);
                    _buttons[i].Visible = true;
                }
                else
                {
                    _buttons[i].Visible = false;
                    _overflow.Add(_buttons[i]);
                }
            }

            _moreButton.Visible = overflow;
            if (overflow)
                _moreButton.SetBounds(x, pad + shown * pitch, item, item);
        }

        /// <summary>
        /// The part of <paramref name="control"/>'s height that is actually on screen:
        /// its own height, cut down by every ancestor window's client area. Word's task
        /// pane container can be shorter than the control it holds.
        /// </summary>
        static int VisibleHeight(Control control)
        {
            int height = control.Height;
            try
            {
                if (!control.IsHandleCreated) return height;
                var rect = control.RectangleToScreen(control.ClientRectangle);
                for (IntPtr parent = NativeMethods.GetParent(control.Handle); parent != IntPtr.Zero; parent = NativeMethods.GetParent(parent))
                {
                    if (!NativeMethods.GetClientRect(parent, out var client)) break;
                    var topLeft = new NativeMethods.POINT { X = client.Left, Y = client.Top };
                    NativeMethods.ClientToScreen(parent, ref topLeft);
                    var parentRect = new Rectangle(topLeft.X, topLeft.Y, client.Right - client.Left, client.Bottom - client.Top);
                    rect.Intersect(parentRect);
                    if (rect.Height <= 0) return height;   // unreadable chain: trust the control
                }
                return Math.Min(height, rect.Height);
            }
            catch { return height; }
        }

        void ShowOverflowMenu()
        {
            _more.Items.Clear();
            _more.RightToLeft = AddinUiLanguage.IsEnglish ? RightToLeft.No : RightToLeft.Yes;
            foreach (var b in _overflow)
            {
                var e = b.Entry;
                var menuItem = new ToolStripMenuItem(e.Label(), b.Icon) { Checked = b.Active };
                menuItem.Click += (_, __) => OnItemClick(e);
                _more.Items.Add(menuItem);
            }
            _more.Show(_moreButton, new Point(DockedLeft ? _moreButton.Width : 0, 0),
                DockedLeft ? ToolStripDropDownDirection.BelowRight : ToolStripDropDownDirection.BelowLeft);
        }

        // ── What other features read ──────────────────────────────────────────

        /// <summary>
        /// The live control of tool <paramref name="id"/> if the user can see it: shown
        /// in this pane, or in a floating window. Never builds one.
        /// </summary>
        internal Control UsableControl(string id)
        {
            var slot = Find(id);
            if (slot == null) return null;
            if (slot.IsPoppedOut) return slot.Control;

            bool paneShown;
            try { paneShown = _pane != null && _pane.Visible; }
            catch { paneShown = false; }
            return paneShown && _active == slot ? slot.Control : null;
        }

        /// <summary>
        /// For the focus-toggle macro (TaskPaneManager.ToggleFocus): brings the open tool
        /// to the user - its floating window if it has one, otherwise this pane - and
        /// returns the control keyboard focus should go into. Null when no tool is open.
        /// </summary>
        internal Control FocusTarget()
        {
            if (_active == null || _active.Control.IsDisposed) return null;
            if (_active.IsPoppedOut)
            {
                _active.PopOut.FocusPopOutWindow();
                return _active.PopOut.PopOutForm;
            }
            ShowTool(_active.Entry);
            return _active.Control;
        }

        /// <summary>
        /// Where keyboard focus counts as "in the pane" for the focus toggle: this host,
        /// and the floating window of any tool that has one.
        /// </summary>
        internal IEnumerable<Control> FocusRoots()
        {
            yield return this;
            foreach (var slot in _slots)
                if (slot.IsPoppedOut) yield return slot.PopOut.PopOutForm;
        }

        /// <summary>
        /// "Duplicate side pane" from the shared pane: the open tool, in a separate pane
        /// of its own - the way to see two tools side by side.
        /// </summary>
        internal CustomTaskPane DuplicateActive()
        {
            var entry = _active?.Entry;
            if (entry == null || entry.Kind != ToolKind.Pane) return null;

            string title = "@" + entry.Label();
            int hwnd = TaskPaneManager.ActiveWindowHandle();
            var existing = Globals.ThisAddIn.CustomTaskPanes.Cast<CustomTaskPane>()
                .FirstOrDefault(p => p != _pane && TaskPaneManager.IsForWindow(p, hwnd) && TitleOf(p) == title);
            if (existing != null)
            {
                TaskPaneManager.Reveal(existing);
                return existing;
            }

            int width = entry.DefaultWidth;
            if (entry.CreateWpfView != null)
                return WpfTaskPane.CreateNew(entry.CreateWpfView(), title, width);

            var pane = TaskPaneManager.CreateNew(entry.CreateControl(), title, width);
            if (pane != null) TaskPaneManager.Reveal(pane);
            return pane;
        }

        static string TitleOf(CustomTaskPane pane)
        {
            try { return pane.Title; }
            catch { return null; }
        }

        /// <summary>
        /// Runs before the pane goes away with its document: what each tool's own pane
        /// got on close - Kitvei Hakodesh writes out its pending state, and floating
        /// windows close rather than outlive their document.
        /// </summary>
        internal async Task PrepareCloseAsync()
        {
            foreach (var slot in _slots.ToList())
                if (slot.Control is KitveiHakodeshLib.AppViewer viewer && !viewer.IsDisposed)
                    await viewer.FlushPersistenceAsync();

            foreach (var slot in _slots.ToList())
                slot.PopOut?.ClosePopOut();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tip.Dispose();
                _more.Dispose();
                _itemMenu.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// The rail strip: its own background, a step away from the pane's, so the rail
        /// reads as a separate bar; a hairline on the inner edge.
        /// </summary>
        sealed class RailPanel : Panel
        {
            public RailPanel()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                using (var b = new SolidBrush(RailButton.StripColor(BackColor, ForeColor)))
                    e.Graphics.FillRectangle(b, ClientRectangle);
                int x = Dock == DockStyle.Right ? 0 : Width - 1;
                using (var pen = new Pen(Color.FromArgb(70, ForeColor)))
                    e.Graphics.DrawLine(pen, x, 0, x, Height);
            }
        }

        static class NativeMethods
        {
            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            public struct RECT { public int Left, Top, Right, Bottom; }

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            public struct POINT { public int X, Y; }

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern IntPtr GetParent(IntPtr hWnd);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
        }
    }
}
