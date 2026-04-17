using KezayitLib.Dictionary;
using KleiKodesh.Ribbon;
using KezayitLib.Pdf;
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
            WordToPdfConverter.HostApplication = this.Application;
            WordThesaurusProvider.HostApplication = this.Application;
        }

        private void ThisAddIn_Shutdown(object sender, System.EventArgs e)
        {
            WordToPdfConverter.CancelHostConversions();
            // Run any pending installer that was deferred during update process
            UpdateChecker.RunPendingInstaller();
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
