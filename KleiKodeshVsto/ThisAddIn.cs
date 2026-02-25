using KleiKodesh.Ribbon;
using UpdateCheckerLib;
using Office = Microsoft.Office.Core;

namespace KleiKodesh
{
    public partial class ThisAddIn
    {
        protected override Office.IRibbonExtensibility CreateRibbonExtensibilityObject()
        {
            return new KeliKodeshRibbon();
        }

        private void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            // Set Word Application for PDF conversion (VSTO mode)
            Zayit.Services.WordToPdfConverter.WordApp = this.Application;

            // Add-in startup - no automatic update checks here
            // Updates are checked when user opens taskpanes
        }

        private void ThisAddIn_Shutdown(object sender, System.EventArgs e)
        {
            // Run any pending installer that was deferred during update process
            UpdateChecker.RunPendingInstaller();

            // Add-in shutdown cleanup
        }

        #region VSTO generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InternalStartup()
        {
            this.Startup += new System.EventHandler(ThisAddIn_Startup);
            this.Shutdown += new System.EventHandler(ThisAddIn_Shutdown);
        }

        #endregion
    }
}
