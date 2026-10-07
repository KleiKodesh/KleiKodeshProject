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
            // The host is reused only if it still ANSWERS. HostApplication is captured once at
            // add-in startup and cleared only at shutdown, so if the underlying proxy is severed
            // while the add-in stays loaded — Word rebuilt its automation server, a
            // crash-recovery cycle, an RPC channel reset — the reference is still non-null but
            // every call on it throws RPC_E_DISCONNECTED forever. Trusting it blindly is what
            // made a broken paste stay broken until the user restarted Word: this branch
            // short-circuits the others, so nothing ever got the chance to rebind. Clearing a
            // dead one is what makes the failure self-healing.
            var host = HostApplication;
            if (host != null)
            {
                if (IsAlive(host)) return host;
                HostApplication = null;
            }

            try
            {
                var running = (Word.Application)Marshal.GetActiveObject("Word.Application");
                // The running-object table can hand back an instance that is already tearing
                // down, which then fails on first use rather than on binding.
                if (running != null && IsAlive(running)) return running;
                return null;
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
            // Deliberately asks FindRunning rather than reading HostApplication directly, so the
            // liveness probe cannot be bypassed on the path that actually drives Word.
            var host = HostApplication;
            if (host != null && IsAlive(host))
            {
                source = Source.Host;
                return host;
            }

            Word.Application? running = FindRunning();
            if (running != null)
            {
                // FindRunning returns the host when it is alive; distinguish so the caller knows
                // whether the instance is theirs to release.
                source = ReferenceEquals(running, HostApplication)
                    ? Source.Host
                    : Source.AlreadyRunning;
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
        /// Whether this instance still answers. Reads one cheap property: a severed RPC channel
        /// throws, a live one does not.
        ///
        /// ALIVE UNLESS PROVEN DEAD — deliberately inverted. Only a severed channel means the
        /// instance is gone; a BUSY Word, or any other COM error, is a Word that is still there.
        /// A false "dead" is the expensive direction, because it discards the user's own
        /// instance and starts a second one beside it.
        /// </summary>
        public static bool IsAlive(Word.Application application)
        {
            if (application == null) return false;

            try
            {
                var unused = application.Build;
                return true;
            }
            catch (COMException ex)
            {
                return !IsDisconnected(ex.HResult);
            }
            catch (InvalidComObjectException)
            {
                return false;   // the RCW was already released
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Whether an HRESULT means the COM channel is severed, as opposed to merely busy.
        ///
        /// Verified against the Windows SDK winerror.h, and getting it wrong is not cosmetic: a
        /// BUSY code listed here reads as a false "dead", which discards the user's Word and
        /// starts a second one beside it. 0x80010005 is RPC_E_CANTCALLOUT_INEXTERNALCALL
        /// ("inside a message filter") — a busy, ALIVE Word — and must never appear below.
        /// </summary>
        private static bool IsDisconnected(int hr)
        {
            return hr == unchecked((int)0x80010108)    // RPC_E_DISCONNECTED
                || hr == unchecked((int)0x800706BA)    // RPC_S_SERVER_UNAVAILABLE
                || hr == unchecked((int)0x800706BE)    // RPC_S_CALL_FAILED
                || hr == unchecked((int)0x80010007)    // RPC_E_SERVER_DIED
                || hr == unchecked((int)0x80010012)    // RPC_E_SERVER_DIED_DNE
                || hr == unchecked((int)0x800401FD);   // CO_E_OBJNOTCONNECTED
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
