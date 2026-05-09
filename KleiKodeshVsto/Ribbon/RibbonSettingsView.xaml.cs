using KleiKodesh.Helpers;
using System.Linq;
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
            // cb.Name IS the registry key (e.g. "KitveiHakodesh_Visible") — do not rename the controls.
            foreach (CheckBox cb in VisibleSettingsPanel.Children.OfType<CheckBox>())
            {
                cb.IsChecked = SettingsManager.GetBool("Ribbon", cb.Name, true);
                cb.Checked   += (_, __) =>
                {
                    SettingsManager.Save("Ribbon", cb.Name, true);
                    _ribbon.InvalidateControl(cb.Name.Replace("_Visible", ""));
                };
                cb.Unchecked += (_, __) =>
                {
                    SettingsManager.Save("Ribbon", cb.Name, false);
                    string componentId = cb.Name.Replace("_Visible", "");
                    _ribbon.InvalidateControl(componentId);
                    // If the corresponding option radio was selected, fall back to Settings
                    var rb = (RadioButton)FindName($"{componentId}_Option");
                    if (rb?.IsChecked == true)
                    {
                        rb.IsChecked = false;
                        Settings_Option.IsChecked = true;
                        SettingsManager.Save("Ribbon", "DefaultButton", "Settings");
                    }
                };
            }

            // rb.Name stripped of "_Option" IS the saved DefaultButton value — do not rename the controls.
            string defaultButtonId = SettingsManager.Get("Ribbon", "DefaultButton", "Settings");
            foreach (RadioButton rb in OptionsSettingsPanel.Children.OfType<RadioButton>())
            {
                rb.IsChecked = rb.Name.Replace("_Option", "") == defaultButtonId;
                rb.Checked += (_, __) =>
                    SettingsManager.Save("Ribbon", "DefaultButton", rb.Name.Replace("_Option", ""));
            }

            ChkTurnOffUpdates.IsChecked = SettingsManager.GetBool("UpdateChecker", "TurnOffUpdates", false);
            ChkTurnOffUpdates.Checked   += (_, __) => SettingsManager.Save("UpdateChecker", "TurnOffUpdates", true);
            ChkTurnOffUpdates.Unchecked += (_, __) => SettingsManager.Save("UpdateChecker", "TurnOffUpdates", false);

            BtnReset.Click += (_, __) =>
            {
                foreach (CheckBox cb in VisibleSettingsPanel.Children.OfType<CheckBox>())
                    if (cb is CheckBox) cb.IsChecked = true;
                foreach (RadioButton rb in OptionsSettingsPanel.Children.OfType<RadioButton>())
                    if (rb is RadioButton) rb.IsChecked = false;
                Settings_Option.IsChecked = true;
                ChkTurnOffUpdates.IsChecked = false;
                SettingsManager.ClearAll();
                MessageBox.Show("התוכנה אופסה בהצלחה - אנא התחל את וורד מחדש");
            };
        }
    }
}
