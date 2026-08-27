using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Puts the CURRENT thread into Windows "background processing mode" for the scope's
    /// lifetime: lowest CPU priority plus VERY-LOW I/O and memory priority, so the thread
    /// only consumes resources nobody else is asking for.
    ///
    /// Why background mode and not just ThreadPriority: the index build's contention with
    /// the rest of the app — searches, the UI, and the catalog/file-search index rebuilds
    /// that an app reset kicks off at the same time — is mostly DISK (the corpus DB read,
    /// segment writes, merge rewrites), and thread priority governs only the CPU. Background
    /// mode (SetThreadPriority with THREAD_MODE_BACKGROUND_BEGIN) is the OS facility for
    /// exactly this trade: the build runs flat out on an idle machine and yields everything
    /// the moment anyone else wants the disk or a core.
    ///
    /// ⚠ NEVER hold background mode across a lock that foreground work waits on. Windows
    /// boosts a starved thread's CPU priority but NEVER lifts its I/O demotion, and a
    /// ReaderWriterLockSlim is a managed lock the OS cannot see, so it gets no priority
    /// inheritance at all. Throttled I/O inside an exclusive section therefore stalls every
    /// waiter for as long as the disk is busy — turning a millisecond commit into seconds.
    /// Use <see cref="Suspend"/> around any such section (see SegmentMerger's commit block).
    ///
    /// MUST be entered and disposed on the same thread — Windows scopes the mode to the
    /// thread itself. Dispose ALWAYS restores (thread-pool threads are reused; leaking the
    /// mode would degrade whatever unrelated work the pool runs next on that thread).
    /// If the OS call is unavailable, falls back to managed ThreadPriority.Lowest, which
    /// still caps the CPU side.
    ///
    /// Nesting is safe: the mode is one per-thread flag with no OS-level counter, so a
    /// nested scope's END would otherwise cancel the outer scope's mode and silently
    /// un-throttle the rest of the build. A thread-static depth counter makes only the
    /// outermost scope touch the mode. Nesting is not hypothetical — Task.Wait can inline a
    /// flush continuation onto a build thread that is already inside a scope.
    /// </summary>
    internal sealed class BackgroundPriorityScope : IDisposable
    {
        private const int THREAD_MODE_BACKGROUND_BEGIN = 0x00010000;
        private const int THREAD_MODE_BACKGROUND_END   = 0x00020000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetThreadPriority(IntPtr hThread, int nPriority);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        /// <summary>Nesting depth of scopes on this thread. Only depth 0→1 begins the OS
        /// mode and only 1→0 ends it, so an inner scope can never un-throttle its outer.</summary>
        [ThreadStatic] private static int _depth;

        /// <summary>Set while <see cref="Suspend"/> has temporarily left the OS mode on this
        /// thread, so a scope entered inside the suspension does not re-enter it.</summary>
        [ThreadStatic] private static bool _suspended;

        private readonly bool _ownsMode;                 // this scope is the outermost one
        private readonly bool _osModeBegun;
        private readonly ThreadPriority _managedPriorityToRestore;
        private readonly bool _managedPriorityChanged;
        private bool _disposed;

        /// <summary>Enters background mode when <paramref name="condition"/> is true;
        /// returns null otherwise — `using` accepts null, so call sites stay one line.</summary>
        public static BackgroundPriorityScope EnterIf(bool condition)
        {
            return condition ? new BackgroundPriorityScope() : null;
        }

        private BackgroundPriorityScope()
        {
            // Inner scope: the outermost one already owns the mode. Do nothing at all, so
            // Dispose has nothing to undo and cannot end a mode it did not begin.
            if (_depth++ > 0 || _suspended) return;

            _ownsMode = true;
            try
            {
                _osModeBegun = SetThreadPriority(GetCurrentThread(), THREAD_MODE_BACKGROUND_BEGIN);
                if (_osModeBegun) return;
            }
            catch (Exception)
            {
                // Entry point missing or blocked — fall through to the managed fallback.
            }

            // Fallback: cap the CPU side only. Save and restore UNCONDITIONALLY — a thread
            // that is already Lowest (a leak from an earlier scope, or another component's
            // doing) must still be restored by us, or the Lowest becomes permanent for the
            // life of that pooled thread and silently starves unrelated work later.
            try
            {
                _managedPriorityToRestore = Thread.CurrentThread.Priority;
                Thread.CurrentThread.Priority = ThreadPriority.Lowest;
                _managedPriorityChanged = true;
            }
            catch (Exception)
            {
                // Priority is not settable on this thread — nothing lowered, nothing to restore.
            }
        }

        /// <summary>
        /// Temporarily leaves background mode on this thread until the returned token is
        /// disposed, then restores it. For the short, exclusive, I/O-doing sections that
        /// foreground threads block on — see the class remarks. Returns null when this
        /// thread is not in background mode, so callers need no branch.
        /// </summary>
        public static IDisposable Suspend()
        {
            if (_depth <= 0 || _suspended) return null;
            return new Suspension();
        }

        private sealed class Suspension : IDisposable
        {
            private readonly bool _ended;                 // OS background mode was lifted
            private readonly bool _priorityRaised;        // managed fallback was lifted instead
            private readonly ThreadPriority _priorityToRestore;
            private bool _disposed;

            internal Suspension()
            {
                _suspended = true;
                try { _ended = SetThreadPriority(GetCurrentThread(), THREAD_MODE_BACKGROUND_END); }
                catch (Exception) { _ended = false; }
                if (_ended) return;

                // The owning scope took the MANAGED fallback, so there was no OS mode to end
                // and the thread is still at ThreadPriority.Lowest. Raising it here is the
                // whole point of the suspension: without this the commit block would run at
                // Lowest while holding the exclusive lock every search waits on — the
                // priority inversion this class exists to avoid, silently un-avoided on any
                // machine where the kernel32 call is unavailable.
                try
                {
                    _priorityToRestore = Thread.CurrentThread.Priority;
                    Thread.CurrentThread.Priority = ThreadPriority.Normal;
                    _priorityRaised = true;
                }
                catch (Exception)
                {
                    // Priority not settable here — nothing raised, nothing to restore.
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _suspended = false;

                if (_priorityRaised)
                {
                    try { Thread.CurrentThread.Priority = _priorityToRestore; }
                    catch (Exception) { }
                    return;
                }
                if (!_ended) return;
                // Re-enter for the remainder of the owning scope. A failure here only means
                // the rest of that scope runs at normal priority — slower for everyone else,
                // never incorrect.
                try { SetThreadPriority(GetCurrentThread(), THREAD_MODE_BACKGROUND_BEGIN); }
                catch (Exception) { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Unwind the depth even for an inner scope, or the outermost one never releases.
            if (_depth > 0) _depth--;
            if (!_ownsMode) return;

            if (_osModeBegun)
            {
                try { SetThreadPriority(GetCurrentThread(), THREAD_MODE_BACKGROUND_END); }
                catch (Exception) { /* nothing left to restore */ }
            }
            else if (_managedPriorityChanged)
            {
                try { Thread.CurrentThread.Priority = _managedPriorityToRestore; }
                catch (Exception) { /* thread is exiting — the priority dies with it */ }
            }
        }
    }
}
