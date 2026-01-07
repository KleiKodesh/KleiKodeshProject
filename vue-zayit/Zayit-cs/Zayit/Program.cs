using System;
using System.Windows.Forms;

namespace Zayit
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var form = new Form
            {
                Height = 700,
                Width = 500,
                StartPosition = FormStartPosition.CenterScreen,
                AutoScaleMode = AutoScaleMode.Dpi
            };

            //var viewer = new ZayitViewer();
            //var uc = new UserControl { Dock = DockStyle.Fill };
            //uc.Controls.Add(viewer);
            form.Controls.Add(new Zayit.Viewer.ZayitViewerHost());
            //form.Controls.Add(viewer);

            Application.Run(form);
        }
    }
}
