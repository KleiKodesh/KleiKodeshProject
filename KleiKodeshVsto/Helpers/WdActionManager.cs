using Microsoft.Office.Interop.Word;
using System;
using System.Windows.Forms;

namespace KleiKodesh.Helpers
{
    public class WdActionManager : IDisposable
    {
        readonly UndoRecord _undoRecord;
        readonly Range _savedRange;
        Timer _timer;

        public WdActionManager(string name = "", bool disableScreenUpdate = true, bool saveRange = false, bool doEvents = true) 
        {
            _undoRecord = Globals.ThisAddIn.Application.UndoRecord;
            _undoRecord.StartCustomRecord(name);

            if (disableScreenUpdate)
                Globals.ThisAddIn.Application.ScreenUpdating = false;

            if (saveRange)
                _savedRange = Globals.ThisAddIn.Application.Selection.Range.Duplicate;

            if (doEvents)
                StartTimer();
        }

        public WdActionManager(bool disableScreenUpdate = true, bool saveRange = false, bool doEvents = true)
        {
            if (disableScreenUpdate)
                Globals.ThisAddIn.Application.ScreenUpdating = false;

            if (saveRange)
                _savedRange = Globals.ThisAddIn.Application.Selection.Range.Duplicate;

            if (doEvents)
                StartTimer();
        }

        public WdActionManager()
        {
            _savedRange = Globals.ThisAddIn.Application.Selection.Range.Duplicate;
        }

        void StartTimer()
        {
            _timer = new Timer { Interval = 1000, Enabled = true };
            _timer.Tick += _timer_Tick;
        }

        private void _timer_Tick(object sender, EventArgs e)
        {
            System.Windows.Forms.Application.DoEvents();
        }

        public void Dispose()
        {
            _undoRecord?.EndCustomRecord();
            _savedRange?.Select();
            Globals.ThisAddIn.Application.ScreenUpdating = true;
            _timer?.Stop();
            _timer?.Dispose();
        }
    }
}
