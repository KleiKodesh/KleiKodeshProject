using FluentChromeTabs;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;

namespace KitveiHakodeshDesktopApp
{
    // Tabs dragged between windows (and off into new ones). The strip drags a label; the
    // tab itself moves page to page: the source page packs it (everything a restart would
    // restore), the target page unpacks it under its own storage, and only once the target
    // has acked does the source close its copy. Any failure leaves the tab in the source.
    // UI thread only, like the rest of the window bookkeeping.
    internal static partial class Program
    {
        // Windows left with nothing by a move (their only tab went elsewhere, or the tab a new
        // window was opened for never arrived): they close for good at once, with no grace
        // period, and only after the hand-over has finished.
        private static readonly HashSet<MainForm> WindowsEmptiedByMove = new HashSet<MainForm>();

        // ── Moves ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Moves a tab of <paramref name="source"/> into <paramref name="target"/> at
        /// <paramref name="position"/> among its pane-1 tabs. <paramref name="arrivingLabel"/>
        /// is the strip label already dropped on the target (null when there is none). True
        /// when the tab moved; false leaves it in the source.
        /// </summary>
        internal static async Task<bool> MoveTabToWindowAsync(MainForm source, string tabId, MainForm target, int position, FluentTab arrivingLabel)
        {
            source.TabsMirror.HoldDepartingTab(tabId);
            source.TabsMirror.Resync();
            // Decided before the move: once the page closes the tab it has one fewer.
            bool sourceKeepsNoTab = source.TabsMirror.PageTabCount <= 1;

            string newTabId = null;
            try
            {
                string moved = await source.Viewer.PackTabAsync(tabId);
                if (moved != null && !target.IsDisposed)
                    newTabId = await target.Viewer.UnpackTabAsync(moved, position);
            }
            catch (Exception) { newTabId = null; }

            if (!target.IsDisposed) target.TabsMirror.SettleArrivingLabel(arrivingLabel, newTabId);
            FinishSourceSide(source, tabId, moved: newTabId != null, sourceKeepsNoTab);
            if (newTabId != null && !target.IsDisposed) target.Activate();
            return newTabId != null;
        }

        // The tab is in the target now (or the move failed): the source closes its copy, or
        // its whole window when that was its only tab; a failed move gives the label back.
        private static void FinishSourceSide(MainForm source, string tabId, bool moved, bool sourceKeepsNoTab)
        {
            if (source.IsDisposed) return;
            if (!moved)
            {
                source.TabsMirror.ReleaseDepartingTab(tabId);
                return;
            }
            source.TabsMirror.TabDeparted(tabId);
            if (sourceKeepsNoTab)
            {
                WindowsEmptiedByMove.Add(source);
                source.Close();
                return;
            }
            source.Viewer.CloseMovedTab(tabId);
        }
    }

#if DEBUG
    /// <summary>
    /// Debug builds only: drives the same move code the strip's drag events call, from a
    /// forwarded launch argument, so a move can be tested without a real mouse drag.
    ///   --debug-tab-move=&lt;sourceWindow&gt;|&lt;tabId&gt;|&lt;targetWindow&gt;|&lt;position&gt;
    /// A window id "-" is the first window (""). No strip label travels (that part is the drag's).
    /// </summary>
    internal static class DebugTabMoves
    {
        internal static bool TryRun(string request)
        {
            const string move = "--debug-tab-move=";
            if (request.StartsWith(move, StringComparison.Ordinal))
            {
                string[] parts = request.Substring(move.Length).Split('|');
                MainForm source = Program.FindWindowForDebug(parts[0]), target = Program.FindWindowForDebug(parts.Length > 2 ? parts[2] : null);
                if (source != null && target != null && parts.Length > 3)
                    _ = Program.MoveTabToWindowAsync(source, parts[1], target, int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture), null);
                return true;
            }
            return false;
        }
    }

    internal static partial class Program
    {
        internal static MainForm FindWindowForDebug(string id)
        {
            if (id == null) return null;
            if (id == "-") id = "";
            foreach (MainForm open in OpenWindows)
                if (open.WindowId == id && !open.IsDisposed) return open;
            return null;
        }
    }
#endif
}
