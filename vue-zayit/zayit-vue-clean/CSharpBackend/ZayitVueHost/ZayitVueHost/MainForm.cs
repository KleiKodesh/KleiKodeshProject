using Kezayit;
using System.Windows.Forms;

namespace ZayitVueHost
{
    public class MainForm : Form
    {
        public MainForm()
        {
            Text = "זית";
            ClientSize = new System.Drawing.Size(600, 750);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;

            var viewer = new AppViewer { Dock = DockStyle.Fill };
            Controls.Add(viewer);


        }
    }
}
