using KleiKodesh.Helpers;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Zayit.Viewer;
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

        void Execute(string id)
        {
            try
            {
                switch (id)
                {
                    case "Kezayit":
                        TaskPaneManager.Show(new Zayit.Viewer.ZayitViewerHost(), "כזית", popOutBehavior: true);
                        break;
                    case "WebSites":
                        WpfTaskPane.Show(new WebSitesLib.WebSitesView(), "דרך האתרים", 510);
                        break;
                    // case "HebrewBooks":
                    //     //WpfTaskPane.Show(new HebrewBooksLib.HebrewBooksView(), LocaleDictionary.Translate(id), 600);
                    //     break;
                    case "KleiKodesh":
                        var control = new DocSeferLib.DocSeferLibView(Globals.ThisAddIn.Application, Globals.Factory);
                        WpfTaskPane.Show(control, "עיצוב תורני", 520);
                        break;
                    case "RegexFind":
                        TaskPaneManager.Show(new KleiKodesh.RegexSearch.RegexFindHost(), "חיפוש רגקס", 600);
                        break;
                    case "DuplicatePane":
                        try { TaskPaneManager.DuplicateCurrent(); } catch { }
                        break;
                    case "Settings":
                        TaskPaneManager.Show(new RibbonSettingsControl(ribbon), "הגדרות כלי קודש", 400);
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
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", control.Id + ".png");
                System.Drawing.Bitmap image = new System.Drawing.Bitmap(path);
                return image;
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
