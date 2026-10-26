using KleiKodeshVstoInstallerWpf.Helpers;
using System.Windows;
using System.Windows.Controls;

namespace KleiKodeshVstoInstallerWpf
{
    /// <summary>
    /// Post-install "What's new" page: the release notes CI embedded in the exe.
    ///
    /// Reached from InstallPage after a successful install via MainWindow.NavigateToWhatsNew(),
    /// which skips this page entirely when no notes are embedded (local builds).
    ///
    /// Navigation:
    ///   Continue → SettingsPage
    ///   Close    → exit (same as SettingsPage's close button)
    /// </summary>
    public partial class WhatsNewPage : Page
    {
        private readonly MainWindow _host;

        public WhatsNewPage(MainWindow host, ReleaseNotes notes)
        {
            InitializeComponent();
            _host = host;
            NotesViewer.FlowDirection = notes.FlowDirection;
            NotesViewer.Document = MarkdownToFlowDocument.Convert(notes.Markdown, notes.FlowDirection);
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e) => _host.NavigateToSettings();

        private void CloseButton_Click(object sender, RoutedEventArgs e) => WordHelper.ExitAfterInstall();
    }
}
