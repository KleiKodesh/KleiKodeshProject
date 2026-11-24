using System.Windows;

namespace DocDesignTaskPaneDebugger
{
    public partial class App : Application
    {
        /// <summary>
        /// "--render &lt;dir&gt;" writes this pane in all four Office themes and
        /// exits, so dark mode can be checked without editing markup.
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            if (KleiKodesh.TaskPaneDebuggerShared.PaneTiming.TryTime(e.Args, () => new MainWindow()))
            {
                Shutdown(0);
                return;
            }

            if (KleiKodesh.TaskPaneDebuggerShared.PaneSnapshot.TryRender(e.Args, () => new MainWindow()))
            {
                Shutdown(0);
                return;
            }

            base.OnStartup(e);

            var window = new MainWindow();
            KleiKodesh.TaskPaneDebuggerShared.PaneSnapshot.ApplyStartupTheme(e.Args, window);
            window.Show();
        }
    }
}
