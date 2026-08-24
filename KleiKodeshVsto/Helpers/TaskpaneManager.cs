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
        private static bool _updateCheckDone = false;

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
                     CreateNew(userControl, title, width, matchOfficeTheme, popOutBehavior);

                Reveal(pane);
                return pane;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error");
                return null;
            }
        }

        /// <summary>
        /// Makes <paramref name="pane"/> visible to the user - which, when its content
        /// has been popped out into a floating window, means focusing that window rather
        /// than showing the pane.
        ///
        /// Popping out reparents the content control into the form and leaves the pane's
        /// own control empty. Setting Visible on the pane in that state shows an empty
        /// task pane, which is what a context-menu search into a popped-out app used to
        /// produce: the search itself reached the live control in the floating window,
        /// but a blank pane opened alongside it.
        /// </summary>
        public static void Reveal(CustomTaskPane pane)
        {
            if (pane == null) return;

            var popOut = TaskPanePopOut.For(pane);
            if (popOut != null && popOut.IsPoppedOut)
                popOut.FocusPopOutWindow();
            else
                pane.Visible = true;
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
                    Reveal(existing);
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
            catch (Exception)
            {
                MessageBox.Show("אין אפשרות לשכפל חלונית צד זו)", "Duplicate TaskPane Error");
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

                TaskPanePopOut popOutHandler = null;
                if (popOutBehavior)
                {
                    popOutHandler = new TaskPanePopOut(userControl, pane);

                    // If the userControl has a method to set the popout toggle action, call it
                    var setPopOutMethod = userControl.GetType().GetMethod("SetPopOutToggleAction");
                    if (setPopOutMethod != null)
                    {
                        setPopOutMethod.Invoke(userControl, new object[] { new Action<bool>(popOutHandler.Toggle) });
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
                if (_updateCheckDone) return;
                _updateCheckDone = true;

                // Keep LastSeenVersion current. There is no longer an "עודכן בהצלחה"
                // notice here: updates run the installer visibly now, so the user has
                // already watched it finish and does not need to be told on next launch.
                UpdateChecker.RecordCurrentVersionAsSeen();

                if (SettingsManager.GetBool("UpdateChecker", "TurnOffUpdates", false)) return;

                // ── Step 1: sync disk check — no network, no threading ──────────────
                // Reads %TEMP%\KleiKodeshSetup.exe version and compares to registry.
                // Arms RunPendingInstaller() and returns the version if newer.
                // Deletes the file if it's stale or already installed.
                var readyVersion = UpdateChecker.GetReadyUpdateVersion();
                if (readyVersion != null)
                {
                    // Sets the expectation for what actually happens now: closing Word
                    // launches the installer, and the user runs it. Saying "יותקן
                    // אוטומטית" would be wrong — the install is no longer silent.
                    UpdateNotificationForm.Show(
                        $"עדכון זמין לגרסה {readyVersion}.\nעם סגירת וורד ייפתח חלון ההתקנה."
                    );
                }

                // ── Step 2: async GitHub check — always runs regardless of Step 1 ──
                // Downloads a newer installer silently if one exists.
                // No UI. PendingInstallerPath is never touched here.
                _ = Task.Run(async () =>
                {
                    try { await UpdateChecker.CheckForUpdateAsync(); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[TaskPaneManager] Update check failed: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TaskPaneManager] CheckForUpdates failed: {ex.Message}");
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
                        try { Globals.ThisAddIn.CustomTaskPanes.Remove(pane); } catch { }
                        userControl?.Dispose();
                    };
            }
            catch { /* Swallow errors silently */ }
        }
    }
}
