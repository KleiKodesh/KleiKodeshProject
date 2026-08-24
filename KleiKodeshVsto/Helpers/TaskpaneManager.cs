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

        // The pane the user most recently opened or brought forward. VSTO exposes no
        // "active pane", and insertion order is not a stand-in for one: with several
        // panes visible at once, the first in the collection is whichever was created
        // earliest, not whichever the user is looking at.
        private static CustomTaskPane _lastRevealed;

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
                var type = userControl.GetType();

                // Match on the control type alone. These panes are created with the
                // two-argument Add, which leaves Window null on purpose so one pane
                // serves every window - this add-in opens a second document in a second
                // window (WordWindowHelper.OpenSoftSnapLeft) and both sides share the
                // pane. The old predicate also compared Window against a live
                // ActiveWindow, which a null never matched, so no pane was ever reused
                // and every ribbon click and context-menu search added another one.
                var pane = FindReusable(panes, type)
                    ?? CreateNew(userControl, title, width, matchOfficeTheme, popOutBehavior);

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
        /// <summary>
        /// Shows the pane hosting a <typeparamref name="T"/>, building one only when
        /// there is no pane to reuse. The eager overload above has to construct the
        /// control just to learn its type - and an AppViewer constructor spins up a
        /// WebView2 and starts initialising it - so a context-menu search into an
        /// already-open pane would build and discard a whole browser control.
        /// </summary>
        public static CustomTaskPane Show<T>(
            Func<T> factory,
            string title,
            int width = 600,
            bool matchOfficeTheme = true,
            bool popOutBehavior = true)
            where T : UserControl
        {
            try
            {
                var pane = FindReusable(Globals.ThisAddIn.CustomTaskPanes, typeof(T))
                    ?? CreateNew(factory(), title, width, matchOfficeTheme, popOutBehavior);

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
        /// The type of a pane's hosted control, or null if the pane is disposed.
        /// A pane whose document has gone throws from every member - and not always
        /// ObjectDisposedException: a released RCW gives COMException (RPC_E_DISCONNECTED)
        /// or InvalidComObjectException instead. A throw from inside a LINQ predicate
        /// escapes the whole lookup and reaches the user as an error dialog on an ordinary
        /// ribbon click, so any failure here means the same thing: not a match.
        /// </summary>
        static Type ControlTypeOf(CustomTaskPane pane)
        {
            try { return pane.Control?.GetType(); }
            catch { return null; }
        }

        /// <summary>
        /// The pane hosting <paramref name="type"/> that the ribbon should bring forward,
        /// or null if there is none to reuse.
        ///
        /// Duplicating a pane creates a second one of the same type, so a type match can
        /// find more than one. Prefer the one the user is actually working in - the one
        /// last brought forward, then any that is shown or popped out - over one that was
        /// merely created first and has since been closed. Revealing a hidden original
        /// while its duplicate is on screen looks like the wrong pane opening.
        /// </summary>
        static CustomTaskPane FindReusable(CustomTaskPaneCollection panes, Type type)
        {
            var matches = panes.Cast<CustomTaskPane>()
                .Where(p => ControlTypeOf(p) == type)
                .ToList();

            return matches.FirstOrDefault(IsLastRevealed)
                ?? matches.FirstOrDefault(IsUsable)
                ?? matches.FirstOrDefault();
        }

        // Whether this is the pane the user last brought forward.
        public static bool IsLastRevealed(CustomTaskPane pane) => pane == _lastRevealed;

        // A pane still worth acting on: alive, and either shown or popped out. Visible
        // alone is not enough - popping out hides the pane by design.
        public static bool IsUsable(CustomTaskPane pane)
        {
            if (ControlTypeOf(pane) == null) return false;
            var popOut = TaskPanePopOut.For(pane);
            return IsVisible(pane) || (popOut != null && popOut.IsPoppedOut);
        }

        // Same disposed-pane hazard as ControlTypeOf.
        static bool IsVisible(CustomTaskPane pane)
        {
            try { return pane.Visible; }
            catch { return false; }
        }

        public static void Reveal(CustomTaskPane pane)
        {
            if (pane == null) return;

            _lastRevealed = pane;

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

                // Prefer the pane the user last brought forward; fall back to the first
                // visible one. Without the preference, duplicating with both Kitvei
                // Hakodesh and Settings open would duplicate whichever was created first
                // rather than the one being used - and a popped-out pane reports itself
                // invisible, so the fallback alone swings on pop-out state.
                var current = panes.FirstOrDefault(p => p == _lastRevealed && IsUsable(p))
                    ?? panes.FirstOrDefault(IsVisible);

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
                        // Close the floating window first if the content was popped out.
                        // Disposing the host below would otherwise leave a live window
                        // owned by a document that no longer exists, with a pop-in target
                        // that has already been disposed.
                        TaskPanePopOut.For(pane)?.ClosePopOut();

                        // Don't leave the "last revealed" pointer on a pane that is going
                        // away - it would keep a dead pane alive and win the preference in
                        // FindReusable over a live one.
                        if (_lastRevealed == pane) _lastRevealed = null;

                        try { Globals.ThisAddIn.CustomTaskPanes.Remove(pane); } catch { }
                        userControl?.Dispose();
                    };
            }
            catch { /* Swallow errors silently */ }
        }
    }
}
