using Microsoft.Office.Tools;
using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace KleiKodesh.Helpers
{
    public static class WpfTaskPane
    {
        public static CustomTaskPane Show(UserControl userControl, string title, int width = 600)
        {
            try
            {
                var panes = Globals.ThisAddIn.CustomTaskPanes;
                var window = Globals.ThisAddIn.Application.ActiveWindow;
                var type = userControl.GetType();

                var pane = panes.Cast<CustomTaskPane>()
                    .FirstOrDefault(p =>
                        p.Control is WinForms.UserControl c &&
                        c.Controls.OfType<ElementHost>().Any(h => h.Child.GetType() == type) &&
                        p.Window == Globals.ThisAddIn.Application.ActiveWindow);

                if (pane != null)
                {
                    pane.Visible = true;
                    return pane;
                }


                var hostControl = new WinForms.UserControl();
                var host = new ElementHost { Dock = WinForms.DockStyle.Fill, Child = userControl };
                hostControl.Controls.Add(host);

                void setColor()
                {
                    var foreColor = hostControl.ForeColor;
                    var adjustedForeColor = Color.FromArgb(foreColor.A, foreColor.B, foreColor.G, foreColor.R);
                    userControl.Foreground = new SolidColorBrush(Color.FromArgb(adjustedForeColor.A, adjustedForeColor.R, adjustedForeColor.G, adjustedForeColor.B));

                    var backColor = hostControl.BackColor;
                    var adjustedBackColor = Color.FromArgb(backColor.A, backColor.B, backColor.G, backColor.R);
                    userControl.Background = new SolidColorBrush(Color.FromArgb(adjustedBackColor.A, adjustedBackColor.R, adjustedBackColor.G, adjustedBackColor.B));
                }

                pane = TaskPaneManager.CreateNew(hostControl, title, width);
                pane.Visible = true;
                
                setColor();
                hostControl.ForeColorChanged += (_, __) => setColor();

                return pane;
            }
            catch (Exception ex)
            {
                WinForms.MessageBox.Show(ex.ToString(), "Error");
                return null;
            }
        }
    }
}
