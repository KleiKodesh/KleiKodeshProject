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

                // Send pipe shutdown to DocumentLocator service immediately so the
                // 1 500 ms exit window runs in the background while we extract other
                // files. AddinInstaller will wait for the remainder of that window
                // (if any) before it tries to overwrite DocumentLocator.Service.exe.
                _ = DocumentLocatorHelper.SendShutdownAsync();

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

                _status.Report("יוצר קיצור דרך...");
                AddinInstaller.CreateKitveiHakodeshShortcut();

                // Register (or re-register) the DocumentLocator Windows Service while
                // we are still a foreground process that can surface a UAC prompt.
                // The VSTO runs inside Word and cannot reliably elevate.
                _status.Report("מתקין שירות אינדקס...");
                await DocumentLocatorHelper.EnsureServiceInstalledAsync();

                // Trigger a background reindex of the file-system search service
                // so it reflects any new files from this install. Fire-and-forget —
                // the service acks immediately and rebuilds without blocking us.
                _ = DocumentLocatorHelper.EnsureServiceRunningAndReindexAsync();

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
