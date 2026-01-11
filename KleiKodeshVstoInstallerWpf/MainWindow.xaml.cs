using KleiKodesh.Helpers;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;

namespace KleiKodeshVstoInstallerWpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            foreach (CheckBox checkBox in VisibleSettingsPanel.Children)
            {
                checkBox.Checked += (s, e) =>
                    SettingsManager.Save("Ribbon", checkBox.Name, true);

                checkBox.Unchecked += (s, e) =>
                    SettingsManager.Save("Ribbon", checkBox.Name, false);

                checkBox.IsChecked = SettingsManager.GetBool("Ribbon", checkBox.Name, true);
            }

            string defaultButtonId = SettingsManager.Get("Ribbon", "DefaultButton", "Settings");
            foreach (RadioButton radioButton in OptionsSettingsPanel.Children)
            {
                radioButton.Checked += (s, e) =>
                    SettingsManager.Save("Ribbon", "DefaultButton", radioButton.Name.Replace("_Option", ""));

                if (radioButton.Name.Contains(defaultButtonId))
                    radioButton.IsChecked = true;
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void Window_TouchDown(object sender, TouchEventArgs e)
        {
            this.DragMove();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Abort();
            else if (e.Key == Key.Return)
                Install();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Abort();
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            Install();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            var email = e.Uri.AbsoluteUri.Replace("mailto:", "");
            Clipboard.SetText(email);
            MessageBox.Show($"כתובת האימייל הועתקה ללוח: {email}");
            e.Handled = true;
        }


        void Install()
        {
            try
            {
                if (Process.GetProcessesByName("WINWORD").Length > 0)
                {
                    MessageBox.Show("אנא סגור את וורד לפני ההתקנה");
                    return;
                }

                new InstallProgressWindow(this).Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"שגיאה בהתקנה: {ex.Message}");
            }
        }

        void Abort()
        {
            // User cancelled - exit with code 1
            Environment.Exit(1);
        }
    }
}