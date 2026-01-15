using Microsoft.Office.Tools;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using UpdateCheckerLib;
using DockPosition = Microsoft.Office.Core.MsoCTPDockPosition;

namespace KleiKodesh.Helpers
{
    public static class TaskPaneManager
    {
        private static bool _updateCheckDone = SettingsManager.GetBool("UpdateChecker", "TurnOffUpdates", false);

        public static CustomTaskPane Show(
            UserControl userControl,
            string title,
            int width = 600,
            bool matchOfficeTheme = true,
            bool popOutBehavior = true)
        {
            try
            {
                var panes = Globals.ThisAddIn.CustomTaskPanes;
                var window = Globals.ThisAddIn.Application.ActiveWindow;
                var type = userControl.GetType();

                var pane = panes.Cast<CustomTaskPane>()
                    .FirstOrDefault(p => p.Control.GetType() == type && p.Window == window) ??
                     CreateNew(userControl, title, width, matchOfficeTheme);

                pane.Visible = true;
                return pane;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error");
                return null;
            }
        }

        public static CustomTaskPane DuplicateCurrent()
        {
            try
            {
                var panes = Globals.ThisAddIn.CustomTaskPanes
                    .Cast<CustomTaskPane>()
                    .ToList();

                var current = panes.FirstOrDefault(p =>
                    p.Window == Globals.ThisAddIn.Application.ActiveWindow &&
                    p.Visible);

                if (current == null)
                    return null;

                string baseTitle = current.Title.TrimStart('@');

                var existing = panes.FirstOrDefault(p =>
                    p != current &&
                    p.Title.TrimStart('@') == baseTitle);

                if (existing != null)
                {
                    existing.Visible = true;
                    return existing;
                }

                if (current.Control is WpfHostControl wpfHost)
                    return WpfTaskPane.DuplicateCurrent(wpfHost, current);

                var controlType = current.Control.GetType();
                var newControl = (UserControl)Activator.CreateInstance(controlType);

                var newPane = CreateNew(
                    newControl,
                    "@" + baseTitle,
                    current.Width
                );

                newPane.Visible = true;
                return newPane;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Duplicate TaskPane Error");
                return null;
            }
        }

        public static CustomTaskPane CreateNew(
           UserControl userControl,
           string title,
           int width = 600,
           bool matchOfficeTheme = true,
           bool popOutBehavior = true)
        {
            try
            {
                CheckForUpdates();

                var panes = Globals.ThisAddIn.CustomTaskPanes;
                var window = Globals.ThisAddIn.Application.ActiveWindow;
                var type = userControl.GetType();
                var pane = panes.Add(userControl, title);

                RestoreDockPosition(pane, type.Name);
                RestoreWidth(pane, userControl, type.Name, width);
                AttachRemoveOnClose(pane, userControl);
                //Globals.ThisAddIn.Application.DocumentChange += () =>
                //{
                //    panes.Remove(pane);
                //    panes.Add(userControl, title);
                //};

                TaskPanePopOut popOutHandler = null;
                if (popOutBehavior)
                {
                    // For ZayitViewerHost, we need to get the actual WebView content, not the host itself
                    Control contentControl = userControl;
                    if (userControl.Controls.Count > 0)
                    {
                        // Use the first child control as the content (typically the WebView)
                        contentControl = userControl.Controls[0];
                    }

                    popOutHandler = new TaskPanePopOut(userControl, contentControl, pane);

                    // If the userControl has a method to set the popout toggle action, call it
                    var setPopOutMethod = userControl.GetType().GetMethod("SetPopOutToggleAction");
                    if (setPopOutMethod != null)
                    {
                        setPopOutMethod.Invoke(userControl, new object[] { new Action(popOutHandler.Toggle) });
                    }
                }

                if (matchOfficeTheme)
                    OfficeThemeWatcher.Attach(userControl);

                return pane;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error");
                return null;
            }
        }

        static void CheckForUpdates()
        {
            try
            {
                // Check for updates on first taskpane open with Hebrew prompt
                if (!_updateCheckDone)
                {
                    _updateCheckDone = true;

                    // Run update check asynchronously without blocking UI
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await UpdateChecker.CheckAndPromptForUpdateAsync(() => Globals.ThisAddIn.Application.Quit());
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"[TaskPaneManager] Update check failed: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"[TaskPaneManager] Update check failed: {ex.Message}");
            }
        }

        static void RestoreDockPosition(CustomTaskPane pane, string type)
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

        static DockPosition GetDefaultDockPosition()
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

        static void AttachRemoveOnClose(CustomTaskPane pane, UserControl userControl)
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
