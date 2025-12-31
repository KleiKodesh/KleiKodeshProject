using KleiKodesh.Helpers;
using System;
using System.IO;
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
        readonly TaskPaneManager taskPaneManager = new TaskPaneManager();

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
            switch (id)
            {
                case "KeZayit":
                    //taskPaneManager.Show(new Zayit.Viewer.ZayitViewerHost(), LocaleDictionary.Translate(id), 600, enablePopout: true);
                    break;
                case "WebSites":
                    //WpfTaskPane.Show(new WebSitesView(), LocaleDictionary.Translate(id), 500);
                    break;
                case "HebrewBooks":
                    //WpfTaskPane.Show(new HebrewBooksLib.HebrewBooksView(), LocaleDictionary.Translate(id), 600);
                    break;
                case "KleiKodesh":
                    //WpfTaskPane.Show(new DocSeferLib.DocSeferLibView(Globals.ThisAddIn.Application, Globals.Factory), LocaleDictionary.Translate(id), 510);
                    break;
                case "RegexFind":
                    taskPaneManager.Show(new KleiKodesh.RegexSearch.RegexFindHost(), "חיפוש רגקס", 600);
                    break;
                case "Settings":
                    taskPaneManager.Show(new RibbonSettingsControl(ribbon), "הגדרות כלי קודש", 400);
                    break;
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
