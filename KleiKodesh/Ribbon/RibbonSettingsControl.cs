using KleiKodesh.Helpers;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace KleiKodesh.Ribbon
{
    public class RibbonSettingsControl : UserControl
    {
        private Microsoft.Office.Core.IRibbonUI _ribbon;

        public RibbonSettingsControl(Microsoft.Office.Core.IRibbonUI ribbon)
        {
            _ribbon = ribbon;
            SuspendLayout();

            Dock = DockStyle.Fill;
            Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            RightToLeft = RightToLeft.Yes;
            AutoSize = true;
            string defaultButtonId = SettingsManager.Get("Ribbon", "DefaultButton", "Settings");

            FlowLayoutPanel flowLayoutPanelTemplate() => new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(16)
            };

            GroupBox groupBoxTemplate(string title) => new GroupBox
            {
                Text = title,
                AutoSize = true,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Padding = new Padding(5),
                Margin = new Padding(0, 0, 0, 16),
                FlatStyle = FlatStyle.Flat
            };

            CheckBox checkBoxTemplate(string text, string name)
            {
                var checkBox = new CheckBox
                {
                    Text = text,
                    Name = name,
                    AutoSize = true,
                    Padding = new Padding(2),
                    Margin = new Padding(0, 8, 0, 8),
                    Checked = SettingsManager.GetBool("Ribbon", name, true)
                };

                checkBox.CheckedChanged += (s, e) =>
                {
                    SettingsManager.Save("Ribbon", checkBox.Name, checkBox.Checked);
                    _ribbon.InvalidateControl(checkBox.Name.Replace("_Visible", ""));
                };
                return checkBox;
            }

            RadioButton radioButtonTemplate(string text, string name)
            {
                var radioButton = new RadioButton
                {
                    Text = text,
                    Name = name,
                    AutoSize = true,
                    Padding = new Padding(2),
                    Margin = new Padding(0, 8, 0, 8),
                    Checked = name.Contains(defaultButtonId)
                };

                radioButton.CheckedChanged += (s, e) =>
                {
                    if (radioButton.Checked)
                        SettingsManager.Save("Ribbon", "DefaultButton", name.Replace("_Option", ""));
                };

                return radioButton;
            }

            // Available components group
            var availableGroup = groupBoxTemplate("רכיבים זמינים");
            var availableFlow = flowLayoutPanelTemplate();
            availableFlow.Controls.AddRange(new Control[] {
                checkBoxTemplate("כזית", "Kezayit_Visible"),
                checkBoxTemplate("היברו בוקס", "HebrewBooks_Visible"),
                checkBoxTemplate("דרך האתרים", "WebSites_Visible"),
                checkBoxTemplate("עיצוב תורני", "KleiKodesh_Visible")
            });
            availableGroup.Controls.Add(availableFlow);

            // Primary button group
            var primaryGroup = groupBoxTemplate("לחצן ראשי");
            var primaryFlow = flowLayoutPanelTemplate();
            primaryFlow.Controls.AddRange(new Control[]
            {
                radioButtonTemplate("כזית", "Kezayit_Option"),
                radioButtonTemplate("היברו בוקס", "HebrewBooks_Option"),
                radioButtonTemplate("דרך האתרים", "WebSites_Option"),
                radioButtonTemplate("עיצוב תורני", "KleiKodesh_Option"),
                radioButtonTemplate("הגדרות", "Settings_Option")
            });
            primaryGroup.Controls.Add(primaryFlow);

            var resetButton = new Button
            {
                Text = "איפוס הגדרות",
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 16, 0, 0),
            };

            resetButton.Click += (s, e) =>
            {
                foreach (var control in availableFlow.Controls)
                    if (control is CheckBox c)
                        c.Checked = true;

                foreach (var control in primaryFlow.Controls)
                    if (control is RadioButton r && r.Name == "Settings_Option")
                        r.Checked = true;
            };

            var rootLayout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Padding = new Padding(16, 0, 0, 0),
                Controls =
                {
                    availableGroup,
                    primaryGroup,
                    resetButton
                }
            };

            Controls.Add(rootLayout);
            ResumeLayout(true);
        }
    }
}
