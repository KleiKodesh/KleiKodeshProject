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
            ApplyTexts();
            InitializeControls();
            InitializeLanguage();
            InitializeMacros();
        }

        // ── VBA macros template ──────────────────────────────────────────────────

        private void InitializeMacros()
        {
            string installed = VbaMacrosTemplate.Installed;
            MacrosNoneRadio.IsChecked    = installed == null;
            MacrosHebrewRadio.IsChecked  = installed == VbaMacrosTemplate.Hebrew;
            MacrosEnglishRadio.IsChecked = installed == VbaMacrosTemplate.English;
            MacrosNoneRadio.Checked    += (_, __) => InstallMacros(null);
            MacrosHebrewRadio.Checked  += (_, __) => InstallMacros(VbaMacrosTemplate.Hebrew);
            MacrosEnglishRadio.Checked += (_, __) => InstallMacros(VbaMacrosTemplate.English);
        }

        // Word holds a loaded STARTUP template open, so unload ours before replacing or
        // removing it. The new one loads on Word's next start - say so.
        private void InstallMacros(string lang)
        {
            bool english = AddinUiLanguage.IsEnglish;
            try
            {
                foreach (Microsoft.Office.Interop.Word.AddIn addIn in Globals.ThisAddIn.Application.AddIns)
                    if (addIn.Name.StartsWith("KleiKodeshMacros", System.StringComparison.OrdinalIgnoreCase))
                        addIn.Delete();

                VbaMacrosTemplate.Install(lang, System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Resources"));
                ShowMacrosMessage(VbaMacrosTemplate.ReopenWordText(english), MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                ShowMacrosMessage(VbaMacrosTemplate.FailedText(english) + "\n" + ex.Message, MessageBoxImage.Error);
            }
        }

        private static void ShowMacrosMessage(string text, MessageBoxImage icon) =>
            MessageBox.Show(text, AddinText.Label("Settings"), MessageBoxButton.OK, icon, MessageBoxResult.OK,
                AddinUiLanguage.IsEnglish ? MessageBoxOptions.None : MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

        // Texts follow General/UiLanguage; component names come from AddinText so they
        // match the ribbon. The checkbox for DocDesign is historically named KleiKodesh_*.
        private void ApplyTexts()
        {
            FlowDirection = AddinUiLanguage.FlowDirection;
            VisibleHeader.Text  = AddinUiLanguage.T("רכיבים זמינים", "Available components");
            OptionsHeader.Text  = AddinUiLanguage.T("לחצן ראשי", "Main button");
            LanguageHeader.Text = AddinUiLanguage.T("שפת ממשק", "Interface language");
            LanguageHint.Text   = AddinUiLanguage.T("ייתכן שחלק מההגדרות יחולו רק לאחר הפעלה מחדש של Word.",
                                                    "Some settings may only take effect after Word is restarted.");
            ChkTurnOffUpdates.Content = AddinUiLanguage.T("כבה בדיקת עדכונים אוטומטית", "Turn off automatic update checks");
            BtnReset.Content          = AddinUiLanguage.T("איפוס התוכנה", "Reset the add-in");

            bool english = AddinUiLanguage.IsEnglish;
            MacrosHeader.Text          = VbaMacrosTemplate.HeaderText(english);
            MacrosNoneRadio.Content    = VbaMacrosTemplate.NoneText(english);
            MacrosHebrewRadio.Content  = VbaMacrosTemplate.HebrewNamesText(english);
            MacrosEnglishRadio.Content = VbaMacrosTemplate.EnglishNamesText(english);
            MacrosHint.Text            = VbaMacrosTemplate.HintText(english);

            foreach (var cb in VisibleSettingsPanel.Children.OfType<CheckBox>())
                cb.Content = ComponentLabel(cb.Name.Replace("_Visible", ""));
            foreach (var rb in OptionsSettingsPanel.Children.OfType<RadioButton>())
                rb.Content = ComponentLabel(rb.Name.Replace("_Option", ""));
        }

        private static string ComponentLabel(string id) =>
            AddinText.Label(id == "KleiKodesh" ? "DocDesign" : id);

        private void InitializeLanguage()
        {
            bool english = AddinUiLanguage.IsEnglish;
            EnglishLanguageRadio.IsChecked = english;
            HebrewLanguageRadio.IsChecked  = !english;
            EnglishLanguageRadio.Checked += (_, __) => SwitchLanguage(AddinUiLanguage.English);
            HebrewLanguageRadio.Checked  += (_, __) => SwitchLanguage(AddinUiLanguage.Hebrew);
        }

        // Saves the language, relabels the ribbon and this view; open panes keep their
        // language until Word restarts, so say so.
        private void SwitchLanguage(string lang)
        {
            if (AddinUiLanguage.Current == lang) return;
            AddinUiLanguage.Set(lang);
            ApplyTexts();
            try { _ribbon?.Invalidate(); } catch { }
            MessageBox.Show(
                AddinUiLanguage.T("ייתכן שחלק מההגדרות יחולו רק לאחר הפעלה מחדש של Word.",
                                  "Some settings may only take effect after Word is restarted."),
                AddinText.Label("Settings"), MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK,
                AddinUiLanguage.IsEnglish ? MessageBoxOptions.None : MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
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
                // Reset never touches the UI language: keep it across the clear.
                string language = AddinUiLanguage.Current;
                SettingsManager.ClearAll();
                AddinUiLanguage.Set(language);
                try { _ribbon?.Invalidate(); } catch { }
                MessageBox.Show(AddinUiLanguage.T("התוכנה אופסה בהצלחה - אנא התחל את וורד מחדש",
                                                  "The add-in was reset - please restart Word"));
            };
        }
    }
}
