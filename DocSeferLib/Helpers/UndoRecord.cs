using Microsoft.Office.Interop.Word;
using System;

namespace DocSeferLib.Helpers
{
    public sealed class UndoRecordHelper : IDisposable
    {
        private readonly Range _originalSelection;
        private readonly int _top;
        private readonly int _left;

        public UndoRecordHelper(string name)
        {
            if (Vsto.UndoRecord != null)
                Vsto.UndoRecord.StartCustomRecord(name);

            Vsto.Application.StatusBar = name;

            _originalSelection = Vsto.Selection.Range.Duplicate;

            var win = Vsto.Application.ActiveWindow;
            _top = win.VerticalPercentScrolled;
            _left = win.HorizontalPercentScrolled;
        }

        public void Dispose()
        {
            if (Vsto.UndoRecord != null)
                Vsto.UndoRecord.EndCustomRecord();

            // Restore selection
            _originalSelection?.Select();

            // Restore scroll
            var win = Vsto.Application.ActiveWindow;
            win.VerticalPercentScrolled = _top;
            win.HorizontalPercentScrolled = _left;

            Vsto.Application.StatusBar = "הפעולה הסתיימה";
        }
    }
}
