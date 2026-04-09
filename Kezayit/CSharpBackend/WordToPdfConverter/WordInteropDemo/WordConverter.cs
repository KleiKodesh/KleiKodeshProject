using System;
using System.IO;
using System.Runtime.InteropServices;
using NetOffice.WordApi;
using NetOffice.WordApi.Enums;

namespace WordInteropDemo
{
    public class WordConverter : IDisposable
    {
        private Application _word;
        private bool _disposed;

        public void Initialize()
        {
            if (_word != null) return;
            _word = new Application();
            _word.Visible = false;
            _word.DisplayAlerts = WdAlertLevel.wdAlertsNone;
        }

        public void Convert(string docxPath, string pdfPath)
        {
            if (_word == null) Initialize();

            if (File.Exists(pdfPath)) File.Delete(pdfPath);

            Document doc = null;
            try
            {
                // Open(FileName, ConfirmConversions, ReadOnly, AddToRecentFiles, ...)
                doc = _word.Documents.Open(docxPath, false, true, false);

                doc.SaveAs2(
                    pdfPath,
                    WdSaveFormat.wdFormatPDF);
            }
            finally
            {
                doc?.Close(false);
                doc?.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _word?.Quit(false); _word?.Dispose(); }
            catch { }
            _word = null;
        }
    }
}
