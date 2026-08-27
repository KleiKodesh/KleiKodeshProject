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
    /// MUST be entered and disposed on the same thread — Windows scopes the mode to the
    /// thread itself. Dispose ALWAYS restores (thread-pool threads are reused; leaking the
    /// mode would degrade whatever unrelated work the pool runs next on that thread).
    /// If the OS call is unavailable, falls back to managed ThreadPriority.Lowest, which
    /// still caps the CPU side.
    /// </summary>
    internal sealed class BackgroundPriorityScope : IDisposable
    {
        private const int THREAD_MODE_BACKGROUND_BEGIN = 0x00010000;
        private const int THREAD_MODE_BACKGROUND_END   = 0x00020000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetThreadPriority(IntPtr hThread, int nPriority);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

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
            try
            {
                // Begin fails (returns false) when the thread is somehow already in
                // background mode — then it is not ours to end, and there is nothing
                // further to lower anyway.
                _osModeBegun = SetThreadPriority(GetCurrentThread(), THREAD_MODE_BACKGROUND_BEGIN);
                if (_osModeBegun) return;
            }
            catch (Exception)
            {
                // Entry point missing or blocked — fall through to the managed fallback.
            }

            if (Thread.CurrentThread.Priority != ThreadPriority.Lowest)
            {
                _managedPriorityToRestore = Thread.CurrentThread.Priority;
                Thread.CurrentThread.Priority = ThreadPriority.Lowest;
                _managedPriorityChanged = true;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

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
