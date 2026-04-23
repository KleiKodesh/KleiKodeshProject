using KiwixLib;
using System.Windows.Forms;

namespace KiwixDemoApp
{
    public class MainForm : Form
    {
        public MainForm()
        {
            Text = "Kiwix";
            Width = 1024;
            Height = 768;
            var viewer = new KiwixWebview { Dock = DockStyle.Fill };
            Controls.Add(viewer);
        }
    }
}