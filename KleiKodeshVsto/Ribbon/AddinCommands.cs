using KleiKodesh.Helpers;
using Nakdan.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Office = Microsoft.Office.Core;

namespace KleiKodesh.Ribbon
{
    /// <summary>
    /// Every command the add-in exposes, in one place. The ribbon callbacks
    /// (KeliKodeshRibbon) and the VBA automation object (VbaCommands) both call
    /// in here, so a command behaves the same whichever way it was invoked.
    /// </summary>
    public static class AddinCommands
    {
        // Set by Ribbon_Load; the settings pane needs it to invalidate controls.
        internal static Office.IRibbonUI Ribbon;

        /// <summary>
        /// Runs a ribbon button command by its control id (see KeliKodeshRibbon.xml).
        /// </summary>
        public static void Run(string id)
        {
            try
            {
                // Panes and the settings view read CurrentUICulture when built.
                AddinUiLanguage.Apply();
                switch (id)
                {
                    case "KitveiHakodesh":
                        ShowKitveiHakodesh();
                        break;
                    case "Kiwix":
                        TaskPaneManager.Show(() => new KiwixLib.KiwixWebview(), AddinText.Label("Kiwix"), 610, popOutBehavior: true);
                        break;
                    case "WebSites":
                        WpfTaskPane.Show(() => new WebSitesLib.UI.WebSitesView(), AddinText.Label("WebSites"), 510);
                        break;
                    // case "HebrewBooks":
                    //     //WpfTaskPane.Show(new HebrewBooksLib.HebrewBooksView(), LocaleDictionary.Translate(id), 600);
                    //     break;
                    case "DocDesign":
                        WpfTaskPane.Show(() => new DocDesign.DocDesignView(Globals.ThisAddIn.Application, Globals.Factory), AddinText.Label("DocDesign"), 520);
                        break;
                    case "Nakdan":
                        WpfTaskPane.Show(() => new NakdanView(Globals.ThisAddIn.Application, Globals.Factory), AddinText.Label("Nakdan"), 520);
                        break;
                    case "RegexFind":
                        WpfTaskPane.Show(() => new RegexFindLib.UI.RegexFindView(Globals.ThisAddIn.Application, Globals.Factory), AddinText.Label("RegexFind"), 600);
                        break;
                    case "DuplicatePane":
                        try { TaskPaneManager.DuplicateCurrent(); } catch { }
                        break;
                    case "OpenChildDoc":
                        WordWindowHelper.OpenSoftSnapLeft();
                        break;
                    case "Settings":
                        //TaskPaneManager.Show(new RibbonSettingsControl(ribbon), "הגדרות כלי קודש", 400);
                        WpfTaskPane.Show(new RibbonSettingsView(Ribbon), AddinText.Label("Settings"), 400);
                        break;
                    case "About":
                        OpenAboutDocument();
                        break;
                    default:
                        MessageBox.Show(AddinUiLanguage.T($"אירעה שגיאה במהלך טעינת {id}", $"An error occurred while loading {id}"));
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        // Push the current Word selection into the
        // Kitvei Hakodesh app as a search. Reuses the running task pane (or
        // launches it) and then calls SearchFromHost on the live AppViewer — the text is
        // stripped of non-word characters app-side before searching.
        public static void SearchSelection(string target)
        {
            try
            {
                // Search items live only on the text-selection menu, so a real
                // selection is expected here.
                string text = Globals.ThisAddIn.Application.Selection?.Text;
                if (string.IsNullOrWhiteSpace(text))
                    return;

                var appViewer = ShowKitveiHakodesh();
                appViewer?.SearchFromHost(text, target);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        // "פתח קישור בכתבי הקודש": open the otzaria:// / kitveihakodeshapp:// /
        // zayit:// link found in the current Word selection's hyperlinks inside the
        // Kitvei Hakodesh app.
        public static void OpenSelectionLink()
        {
            try
            {
                var link = FindSelectionHostLink();
                if (link == null)
                {
                    MessageBox.Show(AddinUiLanguage.T("לא נמצא קישור נתמך בבחירה", "No supported link was found in the selection"));
                    return;
                }

                var appViewer = ShowKitveiHakodesh();
                appViewer?.OpenBookFromHost(link);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        // "השווה עם כתבי הקודש": rewrite the Word selection into the text
        // currently selected in the Kitvei Hakodesh pane, as tracked revisions
        // (issue #244). The pane's selection is read live at click time - a browser
        // keeps its selection when focus leaves it - so there is no stale state to
        // manage. Requires an already-open pane: creating one here would produce an
        // empty viewer with nothing selected.
        public static async void CompareSelectionWithPane()
        {
            try
            {
                var viewers = TaskPaneManager.FindAllUsable(typeof(KitveiHakodeshLib.AppViewer))
                    .Select(p => p.Control as KitveiHakodeshLib.AppViewer)
                    .Where(v => v != null)
                    .ToList();
                if (viewers.Count == 0)
                {
                    MessageBox.Show(AddinUiLanguage.T("יש לפתוח את כתבי הקודש ולסמן בו את הקטע להשוואה",
                        "Open Kitvei Hakodesh and select the passage to compare"), CompareTitle);
                    return;
                }

                // Read every open viewer, not just the most recent one: a browser keeps
                // its selection indefinitely, so with duplicated panes the only reliable
                // sign of which pane the user meant is where a selection actually is.
                // Exactly one non-empty selection decides; several is ambiguous, and
                // guessing would silently rewrite the paragraph into the wrong text.
                var selections = new List<string>();
                foreach (var v in viewers)
                    selections.Add(await v.GetSelectedTextAsync());
                var nonEmpty = selections.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

                if (nonEmpty.Count == 0)
                {
                    MessageBox.Show(AddinUiLanguage.T("לא נמצא טקסט מסומן בכתבי הקודש",
                        "No text is selected in Kitvei Hakodesh"), CompareTitle);
                    return;
                }
                if (nonEmpty.Count > 1)
                {
                    MessageBox.Show(AddinUiLanguage.T("נמצא טקסט מסומן בכמה חלוניות של כתבי הקודש - השאר סימון בחלונית אחת בלבד",
                        "Text is selected in several Kitvei Hakodesh panes - keep a selection in one pane only"), CompareTitle);
                    return;
                }
                string reference = nonEmpty[0];

                // The await resumes on the UI thread through the WinForms context;
                // Invoke covers the rare case it does not, since everything below is
                // Word COM and must run there.
                var marshal = viewers[0];
                if (marshal.InvokeRequired)
                    marshal.Invoke((Action)(() => RunCompare(reference)));
                else
                    RunCompare(reference);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private static void RunCompare(string reference)
        {
            string message = TrackedCompare.ApplyReference(
                Globals.ThisAddIn.Application.Selection, reference);
            if (message != null)
                MessageBox.Show(message, CompareTitle);
        }

        private static string CompareTitle => AddinUiLanguage.T("השוואת טקסט", "Compare Text");
        private static string ErrorTitle   => AddinUiLanguage.T("שגיאה", "Error");

        internal static bool IsKitveiHakodeshEnabled() =>
            SettingsManager.GetBool("Ribbon", "KitveiHakodesh_Visible", true);

        /// <summary>
        /// Returns the first link among the hyperlinks overlapping the current selection
        /// that HostLink can parse (otzaria:// / kitveihakodeshapp:// / zayit://, plus the legacy
        /// seforimapp:// spelling of this app's own scheme), or null if
        /// there is none. Reads
        /// Selection.Hyperlinks (hyperlinks the selection touches, including one the
        /// caret merely sits inside).
        /// </summary>
        internal static KitveiHakodeshLib.HostLink FindSelectionHostLink()
        {
            var selection = Globals.ThisAddIn.Application.Selection;
            if (selection == null)
                return null;

            // Selection.Hyperlinks covers the common cases (text selected over a link,
            // or the caret sitting inside a link). Selection.Range.Hyperlinks is a
            // fallback for boundary cases where the former comes back empty even though
            // the caret's range still contains the link.
            return FirstHostLink(selection.Hyperlinks)
                ?? FirstHostLink(selection.Range?.Hyperlinks);
        }

        private static KitveiHakodeshLib.HostLink FirstHostLink(Microsoft.Office.Interop.Word.Hyperlinks hyperlinks)
        {
            if (hyperlinks == null || hyperlinks.Count == 0)
                return null;

            foreach (Microsoft.Office.Interop.Word.Hyperlink h in hyperlinks)
            {
                var link = KitveiHakodeshLib.HostLink.TryParse(h.Address);
                if (link != null)
                    return link;
            }
            return null;
        }

        /// <summary>
        /// Shows the Kitvei Hakodesh task pane, reusing the already-launched pane if
        /// present (TaskPaneManager.Show handles the reuse), and returns its live
        /// <see cref="KitveiHakodeshLib.AppViewer"/> so callers can drive it directly
        /// without relaunching. The viewer is built through a factory so an open pane
        /// does not pay for a discarded WebView2 on every context-menu search.
        /// </summary>
        public static KitveiHakodeshLib.AppViewer ShowKitveiHakodesh()
        {
            AddinUiLanguage.Apply();
            var pane = TaskPaneManager.Show(
                () => new KitveiHakodeshLib.AppViewer { ShowPopOutButton = true },
                AddinText.Label("KitveiHakodesh"), 610, popOutBehavior: true);
            return pane?.Control as KitveiHakodeshLib.AppViewer;
        }

        /// <summary>
        /// Open the About page (HTML, in the add-in language) in the default browser
        /// </summary>
        private static void OpenAboutDocument()
        {
            try
            {
                string lang = AddinUiLanguage.IsEnglish ? "en" : "he";
                string pagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", $"About.{lang}.html");

                if (!File.Exists(pagePath))
                {
                    MessageBox.Show(
                        AddinUiLanguage.T($"קובץ אודות לא נמצא:\n{pagePath}", $"About file not found:\n{pagePath}"),
                        ErrorTitle,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pagePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    AddinUiLanguage.T($"שגיאה בפתיחת דף אודות:\n{ex.Message}", $"Error opening the About page:\n{ex.Message}"),
                    ErrorTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
