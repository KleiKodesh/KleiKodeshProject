using KleiKodeshVstoInstallerWpf.Helpers;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace KleiKodeshVstoInstallerWpf
{
    public partial class InstallPage : Page
    {
        readonly IProgress<double> _progress;
        readonly IProgress<string> _status;

        public InstallPage()
        {
            InitializeComponent();
            _progress = new Progress<double>(v =>
            {
                ProgressBar.Value = v;
                PercentText.Text  = $"{(int)(v / 124 * 100)}%";
            });
            _status = new Progress<string>(s => StatusText.Text = s);
            Loaded += (_, __) =>
            {
                // Hide close button ג€” user must not abort mid-install
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

                _status.Report("׳׳—׳׳¥ ׳§׳‘׳¦׳™׳...");
                await AddinInstaller.ExtractAsync(_progress);

                // Write the user-edited whitelist (if any) over the extracted default.
                // Must run after ExtractAsync ג€” the install folder must exist first.
                // No-op when PendingWhitelist is null (user did not edit the list).
                AddinInstaller.ApplyPendingWhitelist();

                _status.Report("׳¨׳•׳©׳ ׳×׳•׳¡׳£...");
                await AddinInstaller.RegisterAddInAsync(_progress);

                while (ProgressBar.Value < ProgressBar.Maximum)
                {
                    ProgressBar.Value++;
                    await Task.Delay(10);
                }

                _status.Report("׳©׳•׳׳¨ ׳’׳¨׳¡׳”...");
                AddinInstaller.SaveVersion();

                _status.Report("׳”׳”׳×׳§׳ ׳” ׳”׳•׳©׳׳׳”!");
                await Task.Delay(300);
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
