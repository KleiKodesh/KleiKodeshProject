using System;
using System.Windows.Forms;

namespace Zayit.Viewer
{
    public class ZayitViewerHost : UserControl
    {
        private ZayitViewer _zayitViewer;
        bool _isInitialized = false;

        public ZayitViewerHost()
        {
            // Ensure crisp rendering on high-DPI displays
            AutoScaleMode = AutoScaleMode.Dpi;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | 
                     ControlStyles.AllPaintingInWmPaint | 
                     ControlStyles.UserPaint | 
                     ControlStyles.ResizeRedraw, true);
            
            this.Dock = DockStyle.Fill;
            this.Paint += ZayitViewerHost_Paint;
        }

        private void ZayitViewerHost_Paint(object sender, PaintEventArgs e)
        {
            if (!_isInitialized)
            {
                _zayitViewer = new ZayitViewer();
                Controls.Add(_zayitViewer);
                _isInitialized = true;
            }
        }

        /// <summary>
        /// Called by TaskPaneManager to set the popout toggle action
        /// </summary>
        public void SetPopOutToggleAction(Action popOutToggleAction)
        {
            _zayitViewer?.SetPopOutToggleAction(popOutToggleAction);
        }
    }
}
