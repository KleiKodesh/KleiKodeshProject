using KleiKodesh.Helpers;
using Microsoft.Office.Interop.Word;
using Microsoft.Office.Tools.Ribbon;
using Nakdan;
using Nakdan.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Office = Microsoft.Office.Core;

namespace KleiKodesh.Ribbon
{
    [ComVisible(true)]
    public class KeliKodeshRibbon : Office.IRibbonExtensibility
    {
        private Office.IRibbonUI ribbon;

        public KeliKodeshRibbon()
        {

        }

        #region IRibbonExtensibility Members

        public string GetCustomUI(string ribbonID)
        {
            return GetResourceText("KleiKodesh.Ribbon.KeliKodeshRibbon.xml");
        }

        #endregion

        #region Ribbon Callbacks
        //Create callback methods here. For more information about adding callback methods, visit https://go.microsoft.com/fwlink/?LinkID=271226

        public void Ribbon_Load(Office.IRibbonUI ribbonUI)
        {
            this.ribbon = ribbonUI;
        }

        public void button_Click(Office.IRibbonControl control)
        {
            string id = control.Id == "KleiKodesh_Main"
                ? SettingsManager.Get("Ribbon", "DefaultButton", "Settings")
                : control.Id;

            Execute(id);
        }

        // Right-click context-menu items: push the current Word selection into the
        // Kitvei Hakodesh app as a search. Both items reuse the running task pane (or
        // launch it) and then call SearchFromHost on the live AppViewer — the text is
        // stripped of non-word characters app-side before searching.
        public void contextMenu_Click(Office.IRibbonControl control)
        {
            try
            {
                string target = control.Id.IndexOf("Catalog", StringComparison.Ordinal) >= 0
                    ? "catalog"
                    : "fts";

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

        // Right-click "פתח קישור בכתבי הקודש": open the otzaria:// / kitveihakodeshapp:// /
        // zayit:// link found in the current Word selection's hyperlinks inside the
        // Kitvei Hakodesh app.
        public void openLink_Click(Office.IRibbonControl control)
        {
            try
            {
                var link = FindSelectionHostLink();
                if (link == null)
                {
                    MessageBox.Show("לא נמצא קישור נתמך בבחירה");
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

        // Right-click "השווה עם כתבי הקודש": rewrite the Word selection into the text
        // currently selected in the Kitvei Hakodesh pane, as tracked revisions
        // (issue #244). The pane's selection is read live at click time - a browser
        // keeps its selection when focus leaves it - so there is no stale state to
        // manage. Requires an already-open pane: creating one here would produce an
        // empty viewer with nothing selected.
        public async void compareText_Click(Office.IRibbonControl control)
        {
            try
            {
                var viewers = TaskPaneManager.FindAllUsable(typeof(KitveiHakodeshLib.AppViewer))
                    .Select(p => p.Control as KitveiHakodeshLib.AppViewer)
                    .Where(v => v != null)
                    .ToList();
                if (viewers.Count == 0)
                {
                    MessageBox.Show("יש לפתוח את כתבי הקודש ולסמן בו את הקטע להשוואה", "השוואת טקסט");
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
                    MessageBox.Show("לא נמצא טקסט מסומן בכתבי הקודש", "השוואת טקסט");
                    return;
                }
                if (nonEmpty.Count > 1)
                {
                    MessageBox.Show("נמצא טקסט מסומן בכמה חלוניות של כתבי הקודש - השאר סימון בחלונית אחת בלבד", "השוואת טקסט");
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
                MessageBox.Show(message, "השוואת טקסט");
        }

        // Only show the "open link" item when the selection actually contains a
        // supported link. Office re-evaluates context-menu getVisible on each
        // right-click, so this reflects the current selection every time.
        public bool getContextVisible(Office.IRibbonControl control)
        {
            try { return FindSelectionHostLink() != null; }
            catch { return false; }
        }

        /// <summary>
        /// Returns the first link among the hyperlinks overlapping the current selection
        /// that HostLink can parse (otzaria:// / kitveihakodeshapp:// / zayit://, plus the legacy
        /// seforimapp:// spelling of this app's own scheme), or null if
        /// there is none. Reads
        /// Selection.Hyperlinks (hyperlinks the selection touches, including one the
        /// caret merely sits inside).
        /// </summary>
        private KitveiHakodeshLib.HostLink FindSelectionHostLink()
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
        private KitveiHakodeshLib.AppViewer ShowKitveiHakodesh()
        {
            var pane = TaskPaneManager.Show(
                () => new KitveiHakodeshLib.AppViewer { ShowPopOutButton = true },
                "כתבי הקודש", 610, popOutBehavior: true);
            return pane?.Control as KitveiHakodeshLib.AppViewer;
        }

        void Execute(string id)
        {
            try
            {
                switch (id)
                {
                    case "KitveiHakodesh":
                        ShowKitveiHakodesh();
                        break;
                    case "Kiwix":
                        TaskPaneManager.Show(() => new KiwixLib.KiwixWebview(), "קיוויקס", 610, popOutBehavior: true);
                        break;
                    case "WebSites":
                        WpfTaskPane.Show(() => new WebSitesLib.UI.WebSitesView(), "דרך האתרים", 510);
                        break;
                    // case "HebrewBooks":
                    //     //WpfTaskPane.Show(new HebrewBooksLib.HebrewBooksView(), LocaleDictionary.Translate(id), 600);
                    //     break;
                    case "DocDesign":
                        WpfTaskPane.Show(() => new DocDesign.DocDesignView(Globals.ThisAddIn.Application, Globals.Factory), "עיצוב תורני", 520);
                        break;
                    case "Nakdan":
                        WpfTaskPane.Show(() => new NakdanView(Globals.ThisAddIn.Application, Globals.Factory), "נקדן דיקטה", 520);
                        break;
                    case "RegexFind":
                        WpfTaskPane.Show(() => new RegexFindLib.UI.RegexFindView(Globals.ThisAddIn.Application, Globals.Factory), "חיפוש רגקס", 600);
                        break;
                    case "DuplicatePane":
                        try { TaskPaneManager.DuplicateCurrent(); } catch { }
                        break;
                    case "OpenChildDoc":
                        WordWindowHelper.OpenSoftSnapLeft();
                        break;
                    case "Settings":
                        //TaskPaneManager.Show(new RibbonSettingsControl(ribbon), "הגדרות כלי קודש", 400);
                        WpfTaskPane.Show(new RibbonSettingsView(ribbon), "הגדרות התוסף", 400);
                        break;
                    case "About":
                        OpenAboutDocument();
                        break;
                    default:
                        MessageBox.Show($"אירעה שגיאה במהלך טעינת {id}");
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        public System.Drawing.Image getImage(Office.IRibbonControl control)
            => LoadResourceImage(control.Id);

        // Shared "כתבי הקודש" icon for all Kitvei Hakodesh context-menu items, so they
        // read as one feature. Reusable getImage callback — point any control at it.
        public System.Drawing.Image getKitveiHakodeshIcon(Office.IRibbonControl control)
            => LoadResourceImage("KitveiHakodesh");

        /// <summary>
        /// Loads Resources\&lt;name&gt;.png from the install directory, or null if missing.
        /// </summary>
        private static System.Drawing.Image LoadResourceImage(string name)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", name + ".png");
                return new System.Drawing.Bitmap(path);
            }
            catch
            {
                return null;
            }
        }

        public bool getVisible(Office.IRibbonControl control) =>
            SettingsManager.GetBool("Ribbon", control.Id + "_Visible", true);

        /// <summary>
        /// Open the About document template
        /// </summary>
        private void OpenAboutDocument()
        {
            try
            {
                string templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "About.dotx");

                if (!File.Exists(templatePath))
                {
                    MessageBox.Show(
                        $"קובץ אודות לא נמצא:\n{templatePath}",
                        "שגיאה",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                // Open the template as a new document (not the template itself)
                var doc = Globals.ThisAddIn.Application.Documents.Add(templatePath);
                doc.Activate();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"שגיאה בפתיחת מסמך אודות:\n{ex.Message}",
                    "שגיאה",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        #endregion

        #region Helpers

        private static string GetResourceText(string resourceName)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string[] resourceNames = asm.GetManifestResourceNames();
            for (int i = 0; i < resourceNames.Length; ++i)
            {
                if (string.Compare(resourceName, resourceNames[i], StringComparison.OrdinalIgnoreCase) == 0)
                {
                    using (StreamReader resourceReader = new StreamReader(asm.GetManifestResourceStream(resourceNames[i])))
                    {
                        if (resourceReader != null)
                        {
                            return resourceReader.ReadToEnd();
                        }
                    }
                }
            }
            return null;
        }

        #endregion
    }
}
