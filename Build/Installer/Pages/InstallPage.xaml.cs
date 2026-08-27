using KleiKodeshVstoInstallerWpf.Helpers;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace KleiKodeshVstoInstallerWpf
{
    /// <summary>
    /// Step 2 of the installer flow — runs the actual installation (extract, register, save version).
    ///
    /// Reached two ways:
    ///   - LandingPage "התקן" → NavigateToInstall(showSettingsAfter: true)
    ///   - RepairPage (after cleanup) → NavigateToInstall(showSettingsAfter: true)
    /// Both then navigate to SettingsPage for post-install config. showSettingsAfter:false
    /// exits with code 0 instead; no caller currently uses it.
    ///
    /// Auto-updates come through LandingPage like any other install: --silent is ignored
    /// and the user clicks התקן. See App.xaml.cs for why there is no headless path.
    ///
    /// The close button is hidden for the duration of the install to prevent mid-install abort.
    /// </summary>
    public partial class InstallPage : Page
    {
        readonly IProgress<double> _progress;
        readonly IProgress<string> _status;
        private readonly bool _showSettingsAfter;

        public InstallPage(bool showSettingsAfter = false)
        {
            _showSettingsAfter = showSettingsAfter;
            InitializeComponent();
            _progress = new Progress<double>(v =>
            {
                ProgressBar.Value = v;
                PercentText.Text  = $"{(int)(v / 124 * 100)}%";
            });
            _status = new Progress<string>(s => StatusText.Text = s);
            Loaded += (_, __) =>
            {
                // Hide close button — user must not abort mid-install
                (Window.GetWindow(this) as MainWindow)?.SetCloseButtonVisible(false);
                Install();
            };
        }

        private async void Install()
        {
            try
            {
                // The install sequence itself lives in InstallRunner. Both entry points
                // reach it here: a user who clicked התקן, and the --silent auto-update,
                // which shows this page and auto-runs it without the click.
                bool serviceRegistered = await InstallRunner.RunAsync(_progress, _status);

                // Registering the search service needs one UAC approval the rest of the
                // per-user install doesn't. A declined (or blocked) prompt used to be
                // swallowed here, leaving machines that look installed but have no file
                // search. Offer a single visible retry; either way the install finishes —
                // the add-in works without the service, and file search itself re-offers
                // the elevation on first use.
                if (!serviceRegistered)
                {
                    var retry = MessageBox.Show(
                        "שירות חיפוש הקבצים לא נרשם (נדרש אישור הרשאה). לנסות שוב?",
                        "התקנת שירות אינדקס",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (retry == MessageBoxResult.Yes)
                        await DocumentLocatorHelper.EnsureServiceInstalledAsync();
                }

                while (ProgressBar.Value < ProgressBar.Maximum)
                {
                    ProgressBar.Value++;
                    await Task.Delay(10);
                }

                _status.Report("ההתקנה הושלמה!");
                await Task.Delay(300);
                if (_showSettingsAfter)
                    (Window.GetWindow(this) as MainWindow)?.NavigateToSettings();
                else
                    Environment.Exit(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                Environment.Exit(1);
            }
        }
    }
}
