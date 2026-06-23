using KitveiHakodeshLib.Dictionary;
using KleiKodesh.Ribbon;
using KitveiHakodeshLib.Pdf;
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
            // Set the SQLite native library search directory BEFORE any type that
            // references System.Data.SQLite is first accessed — specifically before
            // KitveiHakodeshLib's static initializers trigger SQLite's own
            // UnsafeNativeMethods static constructor, which calls PreLoadSQLiteDll.
            //
            // System.Data.SQLite checks this env var first (before AppDomain.BaseDirectory),
            // so setting it here guarantees SQLite finds x86\SQLite.Interop.dll or
            // x64\SQLite.Interop.dll in the correct install folder even when running
            // inside a 32-bit Word process where AppDomain.BaseDirectory might differ.
            //
            // See: UnsafeNativeMethods.GetBaseDirectory() in System.Data.SQLite source.
            string installDir = AppDomain.CurrentDomain.BaseDirectory;
            System.Environment.SetEnvironmentVariable(
                "PreLoadSQLite_BaseDirectory", installDir);

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
