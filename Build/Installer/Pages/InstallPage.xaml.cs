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
    ///   - Normal:  LandingPage "התקן" → NavigateToInstall(showSettingsAfter: true)
    ///              After install completes, navigates to SettingsPage (post-install config).
    ///   - Silent:  App.xaml.cs --silent arg → NavigateToInstall(showSettingsAfter: false)
    ///              After install completes, exits with code 0.
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
                if (!System.IO.Directory.Exists(AddinInstaller.InstallPath))
                    System.IO.Directory.CreateDirectory(AddinInstaller.InstallPath);

                _status.Report("מחלץ קבצים...");
                await AddinInstaller.ExtractAsync(_progress);

                _status.Report("רושם תוסף...");
                await AddinInstaller.RegisterAddInAsync(_progress);

                while (ProgressBar.Value < ProgressBar.Maximum)
                {
                    ProgressBar.Value++;
                    await Task.Delay(10);
                }

                _status.Report("שומר גרסה...");
                AddinInstaller.SaveVersion();

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
