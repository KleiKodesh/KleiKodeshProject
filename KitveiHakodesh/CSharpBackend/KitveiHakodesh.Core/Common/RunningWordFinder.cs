using System;
using System.Runtime.InteropServices;
using Word = Microsoft.Office.Interop.Word;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>
    /// Finds a Word instance to work with, in this order: the one hosting us, then one already
    /// running, then a new one.
    ///
    /// EXTRACTED SO NOTHING HAS TO BE HANDED AN INSTANCE. Both the thesaurus and the exporter
    /// used to require a caller to pass in the VSTO Application object, which meant they only
    /// worked inside the add-in and silently returned nothing everywhere else. Word can be
    /// found rather than injected: the running-object table already knows whether it is there.
    /// A caller with a Word of its own still gets to say so — see <see cref="HostApplication"/>.
    ///
    /// The order matters. Reusing the host's instance keeps the user's own documents and undo
    /// history in play; binding to a running instance avoids a second copy of Word appearing on
    /// screen; only when neither exists is a new one worth starting.
    ///
    /// net48 leg only — this is the Office PIA route. The modern leg reaches Word through
    /// DocConvertLib's manual COM/IDispatch code, because native AOT has no PIAs.
    /// </summary>
    public static class RunningWordFinder
    {
        /// <summary>
        /// The instance hosting this code, when there is one. A VSTO add-in sets this during
        /// startup; everything else leaves it null and gets the running-or-new instance instead.
        /// </summary>
        public static Word.Application? HostApplication { get; set; }

        /// <summary>What was found, so a caller knows whether the instance is theirs to close.</summary>
        public enum Source
        {
            /// <summary>The instance hosting us. NEVER quit or release this — it is the user's Word.</summary>
            Host,

            /// <summary>An instance that was already running. Not ours to quit; release when done.</summary>
            AlreadyRunning,

            /// <summary>We started it. Ours to quit.</summary>
            NewlyStarted,
        }

        /// <summary>
        /// An already-running Word, or null when none is running. Never starts one — for a
        /// caller that wants to enrich what it does when Word happens to be open, but must not
        /// launch it just to answer a question.
        /// </summary>
        public static Word.Application? FindRunning()
        {
            if (HostApplication != null) return HostApplication;

            try
            {
                return (Word.Application)Marshal.GetActiveObject("Word.Application");
            }
            catch (COMException)
            {
                return null;   // nothing in the running-object table
            }
        }

        /// <summary>
        /// A Word instance to work with, starting one if necessary.
        /// </summary>
        /// <param name="source">Where it came from — decides whether the caller may quit it.</param>
        public static Word.Application Acquire(out Source source)
        {
            if (HostApplication != null)
            {
                source = Source.Host;
                return HostApplication;
            }

            Word.Application? running = FindRunning();
            if (running != null)
            {
                source = Source.AlreadyRunning;
                return running;
            }

            source = Source.NewlyStarted;
            // Invisible and silent: an instance we started is a tool, and a dialog from it
            // would be a modal box the user cannot connect to anything they did.
            var started = new Word.Application { Visible = false };
            started.DisplayAlerts = Word.WdAlertLevel.wdAlertsNone;
            return started;
        }

        /// <summary>
        /// Releases an instance if it is ours to release. A host instance is left alone
        /// entirely — releasing the add-in's own Application is how a task pane loses its Word.
        /// </summary>
        public static void ReleaseIfNotHost(Word.Application? application, Source source)
        {
            if (application == null || source == Source.Host) return;

            try { Marshal.ReleaseComObject(application); }
            catch (Exception) { /* already released, or the instance is gone */ }
        }
    }
}
