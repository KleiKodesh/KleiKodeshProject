using System.Windows.Forms;

namespace Zayit.Viewer
{
    public class ZayitViewerHost : UserControl
    {
        public ZayitViewerHost()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            this.Dock = DockStyle.Fill;
            var zayitViewer = new ZayitViewer();
            zayitViewer.SetCommandHandler(new ZayitViewerCommands(zayitViewer));
            Controls.Add(zayitViewer);
        }
    }
}
