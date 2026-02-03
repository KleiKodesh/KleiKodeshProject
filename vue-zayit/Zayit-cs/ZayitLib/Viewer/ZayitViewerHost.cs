using System;
using System.Windows.Forms;

namespace Zayit.Viewer
{
    public class ZayitViewerHost : UserControl
    {
        private ZayitViewer _zayitViewer;

        public ZayitViewerHost()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            this.Dock = DockStyle.Fill;
            _zayitViewer = new ZayitViewer();
            Controls.Add(_zayitViewer);
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
