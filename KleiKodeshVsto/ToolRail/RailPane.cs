using KleiKodesh.Helpers;
using KleiKodesh.Ribbon;
using Microsoft.Office.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace KleiKodesh.ToolRail
{
    /// <summary>
    /// Entry point for the rest of the add-in: shows a tool in the shared
    /// "Klei Kodesh" pane of the Word window the user is in, creating that pane on
    /// first use. One pane per Word window, as panes have always been - a pane
    /// belongs to the window that was active when it was added.
    /// </summary>
    public static class RailPane
    {
        internal const string Section = "ToolRail";

        /// <summary>
        /// Whether tools open in the shared pane. Off when the user ticked "each tool
        /// in its own side pane" in Settings, which brings back the separate panes.
        /// </summary>
        public static bool Enabled => !SettingsManager.GetBool(Section, "SeparatePanes", false);

        /// <summary>Whether the ribbon command <paramref name="id"/> is a tool the rail shows.</summary>
        public static bool Handles(string id) => ToolCatalog.IsBuiltInTool(id);

        /// <summary>
        /// Shows tool <paramref name="id"/> in this window's shared pane and returns its
        /// live control, or null when there is no such tool or it could not be built.
        /// </summary>
        public static Control Show(string id)
        {
            var entry = ToolCatalog.Find(id);
            if (entry == null) return null;

            try
            {
                // A program or a macro runs; it never takes a place in the pane, so it
                // must not create one either.
                if (entry.Kind == ToolKind.Program)
                {
                    var existing = HostForActiveWindow(create: false);
                    bool dockedLeft = existing?.DockedLeft
                        ?? TaskPaneManager.GetDefaultDockPosition() == Microsoft.Office.Core.MsoCTPDockPosition.msoCTPDockPositionLeft;
                    ExternalTools.RunProgram(entry, dockedLeft);
                    return null;
                }
                if (entry.Kind == ToolKind.Macro)
                {
                    ExternalTools.RunMacro(entry);
                    return null;
                }

                var host = HostForActiveWindow(create: true);
                return host?.ShowTool(entry);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Error");
                return null;
            }
        }

        static ToolRailHost HostForActiveWindow(bool create)
        {
            int hwnd = TaskPaneManager.ActiveWindowHandle();
            foreach (var pane in Panes())
                if (pane.Control is ToolRailHost host && !host.IsDisposed && TaskPaneManager.IsForWindow(pane, hwnd))
                    return host;

            if (!create) return null;

            var newHost = new ToolRailHost();
            var newPane = Globals.ThisAddIn.CustomTaskPanes.Add(newHost, AddinText.Label("KleiKodesh_Main"));
            newHost.Attach(newPane);
            OfficeThemeWatcher.Attach(newHost);
            TaskPaneManager.AttachRemoveOnClose(newPane, newHost);
            return newHost;
        }

        // Live panes whose control can be read; a pane whose document has closed
        // throws from every member, and that is not an error here.
        static IEnumerable<CustomTaskPane> Panes()
        {
            foreach (CustomTaskPane pane in Globals.ThisAddIn.CustomTaskPanes)
            {
                bool alive;
                try { alive = pane.Control != null; }
                catch { alive = false; }
                if (alive) yield return pane;
            }
        }

        static IEnumerable<ToolRailHost> AllHosts() =>
            Panes().Select(p => p.Control as ToolRailHost).Where(h => h != null && !h.IsDisposed).ToList();

        /// <summary>
        /// Every live <typeparamref name="T"/> of tool <paramref name="id"/> the user can
        /// see, in any window's shared pane or floating window, the one in the pane last
        /// brought forward first. Never builds one.
        /// </summary>
        public static List<T> UsableControls<T>(string id) where T : Control =>
            AllHosts()
                .OrderByDescending(h => TaskPaneManager.IsLastRevealed(h.Pane))
                .Select(h => h.UsableControl(id) as T)
                .Where(c => c != null)
                .ToList();

        /// <summary>Refreshes every rail's icons - after Settings hides or shows a tool.</summary>
        public static void RefreshAll()
        {
            foreach (var host in AllHosts())
                try { host.RefreshButtons(); } catch { }
        }
    }
}
