using Microsoft.Office.Interop.Word;
using RegexFindLib.Search;
using System;
using System.Windows.Forms;

namespace RegexFindLib.Helpers
{
    public class WdActionManager : IDisposable
    {
        readonly IWordService _word;
        readonly UndoRecord _undoRecord;
        readonly Range _savedRange;
        Timer _timer;

        public WdActionManager(IWordService word, string name = "",
                               bool disableScreenUpdate = true,
                               bool saveRange = false, bool doEvents = true)
        {
            _word = word;
            _undoRecord = word.Application.UndoRecord;
            _undoRecord.StartCustomRecord(name);

            if (disableScreenUpdate)
                word.Application.ScreenUpdating = false;

            if (saveRange)
                _savedRange = word.Application.Selection.Range.Duplicate;

            if (doEvents)
                StartTimer();
        }

        public WdActionManager(IWordService word, bool disableScreenUpdate = true,
                               bool saveRange = false, bool doEvents = true)
        {
            _word = word;

            if (disableScreenUpdate)
                word.Application.ScreenUpdating = false;

            if (saveRange)
                _savedRange = word.Application.Selection.Range.Duplicate;

            if (doEvents)
                StartTimer();
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
            if (_word != null) _word.Application.ScreenUpdating = true;
            _timer?.Stop();
            _timer?.Dispose();
        }
    }
}
