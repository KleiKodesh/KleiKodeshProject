using KitveiHakodeshLib.Dictionary;
using KleiKodesh.Ribbon;
using KitveiHakodeshLib.Pdf;
using Office = Microsoft.Office.Core;
using System;

namespace KleiKodesh
{
    public partial class ThisAddIn
    {
        protected override Office.IRibbonExtensibility CreateRibbonExtensibilityObject()
        {
            return new KeliKodeshRibbon();
        }

        // Exposes the add-in's commands to VBA as Application.COMAddIns("KleiKodesh").Object.
        // Office asks for this at load: keep it a bare allocation, no work.
        protected override object RequestComAddInAutomationService()
        {
            return new VbaCommands();
        }

        private void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            WordToPdfConverter.HostApplication = this.Application;
            WordThesaurusProvider.HostApplication = this.Application;
        }

        private void ThisAddIn_Shutdown(object sender, System.EventArgs e)
        {
            WordToPdfConverter.CancelHostConversions();
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
