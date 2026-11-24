using System;
using System.Drawing;
using System.Windows.Forms;

namespace OfficePaneChrome
{
    /// <summary>
    /// A side pane popped out: a <see cref="ChromeWindow"/> in the Office strip's colours, with the side pane's
    /// selector (same menu) as its title and a Fluent pop-in button. Closing it pops the content back in.
    /// </summary>
    internal sealed class PopOutWindow : ChromeWindow
    {
        public bool AllowClose;
        readonly SidePanes.SidePane _side;

        public PopOutWindow(SidePanes.SidePane side)
        {
            _side = side;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;                                   // minimises to the taskbar, not to a stub at the screen corner
            if (side.Strip != null) { TitleBack = side.Strip.Background; TitleInk = side.Strip.Ink; TitleRightToLeft = side.Strip.RightToLeft; }
            Text = side.ActiveTitle;
            TitleButtons.Add(new TitleButton { Glyph = Fluent.PopIn, Tip = SidePanes.PopInTip(TitleRightToLeft), Click = () => SidePanes.PopIn(_side) });
            if (SidePanes.PopOutIcon != null) { Icon = SidePanes.PopOutIcon; ShowIcon = true; }
        }

        public void RefreshTitle() { Text = _side.ActiveTitle; InvalidateTitle(); }

        /// <summary>Full screen = no frame and maximised (the title bar hides itself).</summary>
        public bool IsFullScreen => FormBorderStyle == FormBorderStyle.None;
        public void EnterFullScreen()
        {
            if (IsFullScreen) return;
            _beforeFullScreen = WindowState == FormWindowState.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
            if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;   // else the frame stays
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
        }
        public void LeaveFullScreen() { if (!IsFullScreen) return; FormBorderStyle = FormBorderStyle.Sizable; WindowState = _beforeFullScreen; }
        FormWindowState _beforeFullScreen = FormWindowState.Normal;

        protected override void OnActivated(EventArgs e) { base.OnActivated(e); SidePanes.MakeCurrent(_side); }

        protected override bool IsInteractiveContent => true;
        protected override string TitleContentTip => SidePanes.SelectorTip(TitleRightToLeft);
        protected override int TitleContentWidth() => Fluent.SelectorWidth(_side.ActiveTitle, _side.ActiveIcon, DpiScale);
        protected override void PaintTitleContent(Graphics g, Rectangle r) =>
            Fluent.PaintSelector(g, r, _side.ActiveTitle, _side.ActiveIcon, TitleText, TitleRightToLeft, DpiScale, r.Top + r.Height / 2);
        protected override void OnTitleContentClick(Rectangle screenRect)
        {
            SetTitleContentOpen(true);
            SidePanes.ShowMenu(_side, screenRect, Handle, TitleBack, TitleRightToLeft, DpiScale, () => SetTitleContentOpen(false));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Close = pop back in (the views live in this window). Only Windows shutting down really closes it.
            if (!AllowClose && e.CloseReason != CloseReason.WindowsShutDown) { e.Cancel = true; BeginInvoke((Action)(() => SidePanes.PopIn(_side))); }
            base.OnFormClosing(e);
        }
    }
}
