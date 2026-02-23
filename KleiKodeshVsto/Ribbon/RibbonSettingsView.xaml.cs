using KleiKodesh.Helpers;
using System.Windows;
using System.Windows.Controls;

namespace KleiKodesh.Ribbon
{
    public partial class RibbonSettingsView : UserControl
    {
        private readonly Microsoft.Office.Core.IRibbonUI _ribbon;

        public RibbonSettingsView(Microsoft.Office.Core.IRibbonUI ribbon)
        {
            InitializeComponent();
            _ribbon = ribbon;
            InitializeControls();
        }

        private void InitializeControls()
        {
            var defaultButtonId = SettingsManager.Get("Ribbon", "DefaultButton", "Settings");
            var rbSettings = (RadioButton)FindName("RbSettings");

            foreach (var name in new[] { "Kezayit", "WebSites", "KleiKodesh", "RegexFind" })
            {
                var cb = (CheckBox)FindName($"Chk{name}");
                var rb = (RadioButton)FindName($"Rb{name}");

                cb.IsChecked = SettingsManager.GetBool("Ribbon", $"{name}_Visible", true);
                rb.IsChecked = (name == defaultButtonId);
                rb.Checked += (_, __) => SettingsManager.Save("Ribbon", "DefaultButton", name);

                cb.Checked += (_, __) =>
                {
                    SettingsManager.Save("Ribbon", $"{name}_Visible", true);
                    _ribbon.InvalidateControl(name);
                };
                cb.Unchecked += (_, __) =>
                {
                    SettingsManager.Save("Ribbon", $"{name}_Visible", false);
                    _ribbon.InvalidateControl(name);
                    if (rb.IsChecked == true)
                    {
                        rb.IsChecked = false;
                        rbSettings.IsChecked = true;
                        SettingsManager.Save("Ribbon", "DefaultButton", "Settings");
                    }
                };
            }

            rbSettings.IsChecked = ("Settings" == defaultButtonId);
            rbSettings.Checked += (_, __) => SettingsManager.Save("Ribbon", "DefaultButton", "Settings");

            ChkTurnOffUpdates.IsChecked = SettingsManager.GetBool("UpdateChecker", "TurnOffUpdates", false);
            ChkTurnOffUpdates.Checked += (_, __) => SettingsManager.Save("UpdateChecker", "TurnOffUpdates", true);
            ChkTurnOffUpdates.Unchecked += (_, __) => SettingsManager.Save("UpdateChecker", "TurnOffUpdates", false);

            BtnReset.Click += (_, __) =>
            {
                foreach (var name in new[] { "Kezayit", "WebSites", "KleiKodesh", "RegexFind" })
                {
                    ((CheckBox)FindName($"Chk{name}")).IsChecked = true;
                    ((RadioButton)FindName($"Rb{name}")).IsChecked = false;
                }
                rbSettings.IsChecked = true;
                ChkTurnOffUpdates.IsChecked = false;
                SettingsManager.ClearAll();
                MessageBox.Show("התוכנה אופסה בהצלחה - אנא התחל את וורד מחדש");
            };
        }
    }
}