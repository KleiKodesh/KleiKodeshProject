using Microsoft.Office.Interop.Word;
using System;

namespace KleiKodesh.Helpers
{
    public class RecordUndo : IDisposable
    {
        UndoRecord _undoRecord;
        public RecordUndo(string name, bool disableScreenUpdate = true) 
        {
            _undoRecord = Globals.ThisAddIn.Application.UndoRecord;
            _undoRecord.StartCustomRecord(name);

            if (disableScreenUpdate)
                Globals.ThisAddIn.Application.ScreenUpdating = false;
        }


        public void Dispose()
        {
            _undoRecord.EndCustomRecord();
            Globals.ThisAddIn.Application.ScreenUpdating = true;
        }
    }
}
