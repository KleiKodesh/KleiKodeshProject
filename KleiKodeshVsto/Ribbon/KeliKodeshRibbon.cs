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
            string xml = GetResourceText("KleiKodesh.Ribbon.KeliKodeshRibbon.xml");
            if (xml == null)
                return null;

            // The context menus are generated rather than authored: the same four
            // items repeat across every context menu Word might raise, which is far
            // too much near-identical XML to maintain by hand. See
            // ContextMenuXmlBuilder.
            //
            // Replace would return the document unchanged if the placeholder were
            // ever renamed or reformatted away, and the add-in would come up with a
            // working ribbon and silently no context menus at all. Assert instead:
            // this is a build-time mistake, so it should be loud in dev, not a
            // mystery in the field.
            if (xml.IndexOf(ContextMenusPlaceholder, StringComparison.Ordinal) < 0)
                throw new InvalidOperationException(
                    "KeliKodeshRibbon.xml is missing the " + ContextMenusPlaceholder +
                    " placeholder, so no context menus would be registered.");

            return xml.Replace(ContextMenusPlaceholder, ContextMenuXmlBuilder.Build());
        }

        private const string ContextMenusPlaceholder = "<!--CONTEXT_MENUS-->";

        #endregion

        #region Ribbon Callbacks
        //Create callback methods here. For more information about adding callback methods, visit https://go.microsoft.com/fwlink/?LinkID=271226

        public void Ribbon_Load(Office.IRibbonUI ribbonUI)
        {
            this.ribbon = ribbonUI;
            AddinCommands.Ribbon = ribbonUI;
        }

        public void button_Click(Office.IRibbonControl control)
        {
            string id = control.Id == "KleiKodesh_Main"
                ? SettingsManager.Get("Ribbon", "DefaultButton", "Settings")
                : control.Id;

            AddinCommands.Run(id);
        }

        // Right-click search items: the Catalog item searches the catalog, the other full text.
        public void contextMenu_Click(Office.IRibbonControl control) =>
            AddinCommands.SearchSelection(
                control.Id.IndexOf("Catalog", StringComparison.Ordinal) >= 0 ? "catalog" : "fts");

        public void openLink_Click(Office.IRibbonControl control) =>
            AddinCommands.OpenSelectionLink();

        public void compareText_Click(Office.IRibbonControl control) =>
            AddinCommands.CompareSelectionWithPane();

        // Only show the "open link" item when the selection actually contains a
        // supported link. Office re-evaluates context-menu getVisible on each
        // right-click, so this reflects the current selection every time.
        public bool getContextVisible(Office.IRibbonControl control)
        {
            if (!AddinCommands.IsKitveiHakodeshEnabled())
                return false;
            try { return AddinCommands.FindSelectionHostLink() != null; }
            catch { return false; }
        }

        // Context-menu items belong to the Kitvei Hakodesh feature: hide them when
        // the user unchecks it in the settings pane.
        public bool getKitveiHakodeshContextVisible(Office.IRibbonControl control) =>
            AddinCommands.IsKitveiHakodeshEnabled();

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

        // Ribbon text comes from AddinText, so a language switch is one table.
        public string getLabel(Office.IRibbonControl control) => AddinText.Label(control.Id);

        public string getSupertip(Office.IRibbonControl control) => AddinText.Tip(control.Id);

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
