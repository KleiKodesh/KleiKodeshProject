using System.Windows.Forms;

namespace ZayitVueHost
{
    public class MainForm : Form
    {
        public MainForm()
        {
            Text = "זית";
            ClientSize = new System.Drawing.Size(480, 850);
            AutoScaleMode = AutoScaleMode.Font;

            var viewer = new AppViewer { Dock = DockStyle.Fill };
            Controls.Add(viewer);
        }
    }
}
