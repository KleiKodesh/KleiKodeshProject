using Microsoft.Office.Interop.Word;
using System;
using System.Windows.Forms;

namespace RegexFindLib.Helpers
{
    public class WdActionManager : IDisposable
    {
        readonly UndoRecord _undoRecord;
        readonly Range _savedRange;
        Timer _timer;

        public WdActionManager(string name = "", bool disableScreenUpdate = true,
                               bool saveRange = false, bool doEvents = true)
        {
            _undoRecord = Vsto.Application.UndoRecord;
            _undoRecord.StartCustomRecord(name);

            if (disableScreenUpdate)
                Vsto.Application.ScreenUpdating = false;

            if (saveRange)
                _savedRange = Vsto.Application.Selection.Range.Duplicate;

            if (doEvents)
                StartTimer();
        }

        public WdActionManager(bool disableScreenUpdate = true, bool saveRange = false, bool doEvents = true)
        {
            if (disableScreenUpdate)
                Vsto.Application.ScreenUpdating = false;

            if (saveRange)
                _savedRange = Vsto.Application.Selection.Range.Duplicate;

            if (doEvents)
                StartTimer();
        }

        public WdActionManager()
        {
            _savedRange = Vsto.Application.Selection.Range.Duplicate;
        }

        void StartTimer()
        {
            _timer = new Timer { Interval = 1000, Enabled = true };
            _timer.Tick += (s, e) => System.Windows.Forms.Application.DoEvents();
        }

        public void Dispose()
        {
            _undoRecord?.EndCustomRecord();
            _savedRange?.Select();
            Vsto.Application.ScreenUpdating = true;
            _timer?.Stop();
            _timer?.Dispose();
        }
    }
}
