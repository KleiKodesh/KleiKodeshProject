using System.Windows.Forms;
using ZayitVueHost.viewer;

namespace ZayitVueHost
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            Controls.Add(new ZayitViewer { Dock = DockStyle.Fill });
        }
    }
}
