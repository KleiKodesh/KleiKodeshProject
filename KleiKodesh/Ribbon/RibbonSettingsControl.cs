using KleiKodesh.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KleiKodesh.Settings
{
    public partial class RibbonSettingsControl : UserControl
    {
        private Microsoft.Office.Core.IRibbonUI _ribbon;
        public RibbonSettingsControl(Microsoft.Office.Core.IRibbonUI ribbon)
        {
            _ribbon = ribbon;

            InitializeComponent();

            var defaultButton = SettingsManager.Get("Ribbon", "DefaultButton", "Settings");
            foreach (var control in flowLayoutPanel.Controls)
            {
                if (control is CheckBox c)
                {
                    c.Checked = SettingsManager.GetBool("Ribbon", c.Name, true);
                    c.CheckedChanged += (s, e) =>
                    {
                        SettingsManager.Save("Ribbon", c.Name, c.Checked);
                        _ribbon.InvalidateControl(c.Name.Replace("_Visible", ""));
                    };
                }
                else if (control is RadioButton r)
                {
                    if (r.Name.Contains(defaultButton))
                        r.Checked = true;

                    r.CheckedChanged += (s, e) =>
                        SettingsManager.Save("Ribbon", "DefaultButton", r.Name.Replace("_Option", ""));
                }
            }
        }

        private void ResetButton_Click(object sender, EventArgs e)
        {
            foreach (var control in flowLayoutPanel.Controls)
            {
                SettingsManager.Save("Ribbon", "DefaultButton", "Settings");
                if (control is CheckBox c)
                {
                    c.Checked = true;
                }
                else if (control is RadioButton r)
                {
                    if (r.Name == "Settings_Option")
                        r.Checked = true;
                }
            }

            SettingsManager.ClearAll();
        }
    }
}
