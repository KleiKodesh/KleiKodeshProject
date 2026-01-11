using KleiKodesh.Helpers;
using System.Drawing;
using System.Windows.Forms;

namespace KleiKodesh.Ribbon
{
    public class RibbonSettingsControl : UserControl
    {
        private readonly Microsoft.Office.Core.IRibbonUI _ribbon;

        public RibbonSettingsControl(Microsoft.Office.Core.IRibbonUI ribbon)
        {
            _ribbon = ribbon;
            SuspendLayout();

            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new SizeF(96F, 96F);

            Dock = DockStyle.Fill;
            this.Font = SystemFonts.MessageBoxFont;
            RightToLeft = RightToLeft.Yes;
            AutoScroll = true;

            var defaultButtonId = SettingsManager.Get("Ribbon", "DefaultButton", "Settings");

            // Compact templates
            FlowLayoutPanel CreateFlow() => new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };

            GroupBox CreateGroup(string title) => new GroupBox
            {
                Text = title,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 15),
                Padding = new Padding(15, 10, 15, 10),
                FlatStyle = FlatStyle.Flat
            };

            CheckBox CreateCheckBox(string text, string name) => new CheckBox
            {
                Text = text,
                Name = name,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4),
                Checked = SettingsManager.GetBool("Ribbon", name, true)
            };

            RadioButton CreateRadioButton(string text, string name) => new RadioButton
            {
                Text = text,
                Name = name,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4),
                Checked = name.Contains(defaultButtonId)
            };

            // Available components
            var availableGroup = CreateGroup("רכיבים זמינים");
            var availableFlow = CreateFlow();

            var checkBoxes = new[] {
                CreateCheckBox("כזית", "Kezayit_Visible"),
                //CreateCheckBox("היברו בוקס", "HebrewBooks_Visible"),
                CreateCheckBox("דרך האתרים", "WebSites_Visible"),
                CreateCheckBox("עיצוב תורני", "KleiKodesh_Visible"),
                CreateCheckBox("חיפוש רגקס", "RegexFind_Visible")
            };

            foreach (var cb in checkBoxes)
            {
                cb.CheckedChanged += (s, e) =>
                {
                    SettingsManager.Save("Ribbon", cb.Name, cb.Checked);
                    _ribbon.InvalidateControl(cb.Name.Replace("_Visible", ""));
                };
                availableFlow.Controls.Add(cb);
            }
            availableGroup.Controls.Add(availableFlow);

            // Primary button
            var primaryGroup = CreateGroup("לחצן ראשי");
            var primaryFlow = CreateFlow();

            var radioButtons = new[] {
                CreateRadioButton("כזית", "Kezayit_Option"),
                //CreateRadioButton("היברו בוקס", "HebrewBooks_Option"),
                CreateRadioButton("דרך האתרים", "WebSites_Option"),
                CreateRadioButton("עיצוב תורני", "KleiKodesh_Option"),
                CreateRadioButton("חיפוש רגקס", "RegexFind_Option"),
                CreateRadioButton("הגדרות", "Settings_Option"),
            };

            foreach (var rb in radioButtons)
            {
                rb.CheckedChanged += (s, e) =>
                {
                    if (rb.Checked)
                        SettingsManager.Save("Ribbon", "DefaultButton", rb.Name.Replace("_Option", ""));
                };
                primaryFlow.Controls.Add(rb);
            }
            primaryGroup.Controls.Add(primaryFlow);


            var turnOffUpdatesCheckBox = new CheckBox
            {
                Text = "כבה בדיקת עדכונים אוטומטית",
                Checked = SettingsManager.GetBool("UpdateChecker", "TurnOffUpdates", false),
                AutoSize = true,
                Margin = new Padding(0, 10, 0, 4),
            };

            turnOffUpdatesCheckBox.CheckedChanged += (_, __) =>
                SettingsManager.Save("UpdateChecker", "TurnOffUpdates", turnOffUpdatesCheckBox.Checked);

            var resetButton = new Button
            {
                Text = "איפוס התוכנה",
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                Margin = Padding.Empty
            };

            resetButton.Click += (s, e) =>
            {
                foreach (var cb in checkBoxes) cb.Checked = true;
                foreach (var rb in radioButtons)
                    if (rb.Name == "Settings_Option") rb.Checked = true;

                SettingsManager.ClearAll();
                MessageBox.Show("התוכנה אופסה בהצלחה - אנא התחל את וורד מחדש");
            };

            Controls.Add(new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Padding = new Padding(12),
                Controls =
                {
                    availableGroup,
                    primaryGroup,
                    resetButton,
                    turnOffUpdatesCheckBox,
                }
            });

            ResumeLayout(true);
        }
    }
}
