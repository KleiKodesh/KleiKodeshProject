using System.Windows;

namespace DocDesignDemo
{
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
