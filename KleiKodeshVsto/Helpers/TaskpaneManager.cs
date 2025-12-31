using Microsoft.Office.Interop.Word;
using Microsoft.Office.Tools;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using KleiKodesh.Helpers;
using DockPosition = Microsoft.Office.Core.MsoCTPDockPosition;

namespace KleiKodesh.Helpers
{
    public class TaskPaneManager
    {
        private static bool _updateCheckDone = false;
        
        public CustomTaskPane Show(
            UserControl userControl,
            string title,
            int width = 600,
            bool matchOfficeTheme = true,
            bool popOutBehavior = false)
        {
            try
            {
                // // Check for updates on first taskpane open - DISABLED for now to prevent crashes
                // if (!_updateCheckDone)
                // {
                //     _updateCheckDone = true;
                //     Debug.WriteLine("[TaskPaneManager] First taskpane open - update check disabled for stability");
                    
                //     // TODO: Re-enable update check once we find a safer approach
                //     // The PowerShell script launch is causing Word crashes
                // }
                
                var panes = Globals.ThisAddIn.CustomTaskPanes;
                var window = Globals.ThisAddIn.Application.ActiveWindow;
                var type = userControl.GetType();

                var pane = panes.Cast<CustomTaskPane>()
                    .FirstOrDefault(p => p.Control.GetType() == type && p.Window == window);

                if (pane == null)
                {
                    pane = panes.Add(userControl, title);

                    RestoreDockPosition(pane, type.Name);
                    RestoreWidth(pane, userControl, type.Name, width);
                    AttachRemoveOnClose(pane, userControl);
                    //Globals.ThisAddIn.Application.DocumentChange += () =>
                    //{
                    //    panes.Remove(pane);
                    //    panes.Add(userControl, title);
                    //};

                    if (popOutBehavior)
                        new TaskPanePopOut(userControl, userControl, pane);

                    if (matchOfficeTheme)
                        OfficeThemeWatcher.Attach(userControl);
                }

                pane.Visible = true;
                return pane;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error");
                return null;
            }
        }

        void RestoreDockPosition(CustomTaskPane pane, string type)
        {
            try
            {
                var defaultPos = GetDefaultDockPosition();

                pane.DockPosition = SettingsManager.GetEnum(
                    type,
                    "DockPosition",
                    defaultPos
                );

                pane.DockPositionChanged += (s, e) =>
                    SettingsManager.Save(type, "DockPosition", pane.DockPosition);
            }
            catch
            {
                pane.DockPosition = DockPosition.msoCTPDockPositionLeft;
            }
        }

        DockPosition GetDefaultDockPosition()
        {
            int uiLang = Globals.ThisAddIn.Application
                .LanguageSettings
                .LanguageID[Microsoft.Office.Core.MsoAppLanguageID.msoLanguageIDUI];

            return (uiLang == 1037 || uiLang == 1025)
                ? DockPosition.msoCTPDockPositionLeft
                : DockPosition.msoCTPDockPositionRight;
        }

        static void RestoreWidth(
            CustomTaskPane pane,
            UserControl userControl,
            string type,
            int defaultWidth)
        {
            try
            {
                pane.Width = SettingsManager.GetInt(type, "TaskPaneWidth", defaultWidth);

                userControl.SizeChanged += (s, e) =>
                    SettingsManager.Save(type, "TaskPaneWidth", pane.Width);
            }
            catch { /* Swallow errors silently */ }
        }

        private static void AttachRemoveOnClose(CustomTaskPane pane, UserControl userControl)
        {
            try
            {
                Globals.Factory.GetVstoObject(Globals.ThisAddIn.Application.ActiveDocument)
                    .CloseEvent += () =>
                    {
                        Globals.ThisAddIn.CustomTaskPanes.Remove(pane);
                        userControl.Dispose();
                    };
            }
            catch { /* Swallow errors silently */ }
        }
    }
}
