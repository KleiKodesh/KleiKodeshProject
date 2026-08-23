using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace NakdanDemo
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// "--render &lt;dir&gt;" writes this pane in all four Office themes and
        /// exits, so dark mode can be checked without editing markup.
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            if (KleiKodesh.DemoShared.PaneSnapshot.TryRender(e.Args, () => new MainWindow()))
            {
                Shutdown(0);
                return;
            }

            base.OnStartup(e);
        }
    }
}
