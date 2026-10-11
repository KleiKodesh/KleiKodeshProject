using Microsoft.Office.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DockPosition = Microsoft.Office.Core.MsoCTPDockPosition;
using Word = Microsoft.Office.Interop.Word;

namespace KleiKodesh.Helpers
{
    public static class TaskPaneManager
    {

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

                // Reuse a pane of this control type in this window, and otherwise build
                // one. Both halves matter: matching on type alone reused another
                // document's pane and nothing appeared here, while never matching at all
                // added a fresh pane on every ribbon click and context-menu search.
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
            var activeHwnd = ActiveWindowHandle();

            var matches = panes.Cast<CustomTaskPane>()
                .Where(p => ControlTypeOf(p) == type && IsForWindow(p, activeHwnd))
                .ToList();

            return matches.FirstOrDefault(IsLastRevealed)
                ?? matches.FirstOrDefault(IsUsable)
                ?? matches.FirstOrDefault();
        }

        /// <summary>
        /// The window handle of the window the user is in, or zero if there is none.
        /// Read once per lookup rather than per pane: each read is a COM call, and two
        /// reads inside one lookup could disagree.
        /// </summary>
        public static int ActiveWindowHandle()
        {
            try { return Globals.ThisAddIn.Application.ActiveWindow.Hwnd; }
            catch { return 0; }
        }

        /// <summary>
        /// Whether <paramref name="pane"/> belongs to the window with
        /// <paramref name="activeHwnd"/>.
        ///
        /// A pane is owned by the window that was active when it was added, and Word
        /// only ever draws it there. Reusing one across documents is what stopped the
        /// pane appearing in a second document: the lookup matched the first document's
        /// pane on control type, revealed it, and Word showed it in the document it
        /// belonged to - so nothing opened where the user had asked, with no error.
        ///
        /// Compare handles, not the objects. CustomTaskPane.Window is typed object, so
        /// `pane.Window == someWindow` is a reference comparison, and two fetches of the
        /// same Word window can hand back two different wrappers - which compares false
        /// for the very window the pane belongs to. That failure mode is silent and
        /// total: no pane is ever reused, and a new one is added on every ribbon click.
        ///
        /// Same disposed-pane hazard as ControlTypeOf: a pane whose document has gone
        /// throws from Window, and a throw here is not a match.
        /// </summary>
        public static bool IsForWindow(CustomTaskPane pane, int activeHwnd)
        {
            if (activeHwnd == 0) return false;

            try { return (pane.Window as Word.Window)?.Hwnd == activeHwnd; }
            catch { return false; }
        }

        /// <summary>
        /// Every live pane hosting <paramref name="controlType"/> that is shown or
        /// popped out, the one the user last brought forward first. Never creates one -
        /// for features that read from a pane rather than show it. Returns all
        /// candidates rather than picking one: with duplicated panes only the caller
        /// can tell which pane's content is the one the user meant.
        /// </summary>
        public static List<CustomTaskPane> FindAllUsable(Type controlType) =>
            Globals.ThisAddIn.CustomTaskPanes.Cast<CustomTaskPane>()
                .Where(p => ControlTypeOf(p) == controlType && IsUsable(p))
                .OrderByDescending(IsLastRevealed)
                .ToList();

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
                // Only panes in the window the user is in are candidates - duplicating
                // must not pick up the pane belonging to another open document.
                var activeHwnd = ActiveWindowHandle();

                var current = panes.FirstOrDefault(p => p == _lastRevealed && IsUsable(p) && IsForWindow(p, activeHwnd))
                    ?? panes.FirstOrDefault(p => IsVisible(p) && IsForWindow(p, activeHwnd));

                if (current == null)
                    return null;

                string baseTitle = current.Title.TrimStart('@');

                var existing = panes.FirstOrDefault(p =>
                    p != current &&
                    IsForWindow(p, activeHwnd) &&
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
