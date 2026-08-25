using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>
    /// Calls back when anything under a registry key changes.
    ///
    /// Event-driven, not polled: <c>RegNotifyChangeKeyValue</c> parks one background thread
    /// inside the kernel until a change happens, so idling costs nothing and each wake costs a
    /// single notification. There is no interval to tune and no change that gets missed between
    /// polls.
    ///
    /// A key that does not exist yet is still watchable: it walks up to the deepest ancestor
    /// that DOES exist and watches that with subtree notification, so the key being created is
    /// itself the event. On the next re-arm it drops back down to the now-existing key, which is
    /// a quieter watch.
    ///
    /// The callback says only "something under there changed" — reading the value and deciding
    /// what it means is the caller's business.
    /// </summary>
    public sealed class RegistryValueWatcher : IDisposable
    {
        private const uint NotifyChangeName = 0x1;     // a subkey was created or deleted
        private const uint NotifyChangeLastSet = 0x4;  // a value was written

        /// <summary>How long to wait before re-arming after the kernel refuses the watch. Long,
        /// because the causes are transient-but-not-brief: a key mid-deletion, a handle limit,
        /// a policy hiccup.</summary>
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

        private readonly string _keyPath;
        private readonly Action _onChanged;
        private readonly string _threadName;

        private Thread? _thread;
        private RegistryKey? _watchedKey;
        private volatile bool _stopping;

        /// <param name="keyPath">Path under HKEY_CURRENT_USER, e.g.
        /// <c>Software\Vendor\Product</c>. It need not exist yet.</param>
        /// <param name="onChanged">Called on the watcher's own background thread, once per
        /// change notification.</param>
        /// <param name="threadName">Names the background thread in a debugger. Worth setting
        /// when more than one watcher is running.</param>
        public RegistryValueWatcher(string keyPath, Action onChanged, string threadName = "registry-watch")
        {
            if (string.IsNullOrWhiteSpace(keyPath))
                throw new ArgumentException("keyPath is required", nameof(keyPath));

            _keyPath = keyPath;
            _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
            _threadName = threadName;
        }

        public void Start()
        {
            if (_thread != null) return;

            _thread = new Thread(WatchLoop)
            {
                // Background: the thread spends its life blocked in the kernel, and closing the
                // watched key in Dispose is what releases it. It must never hold up process exit.
                IsBackground = true,
                Name = _threadName,
            };
            _thread.Start();
        }

        private void WatchLoop()
        {
            while (!_stopping)
            {
                RegistryKey? key = null;
                try
                {
                    key = OpenDeepestExistingAncestor(out bool watchSubtree);
                    if (key == null) return; // no HKCU\Software at all — nothing to watch, ever
                    _watchedKey = key;

                    int result = RegNotifyChangeKeyValue(
                        key.Handle, watchSubtree,
                        NotifyChangeName | NotifyChangeLastSet, IntPtr.Zero, false);

                    if (_stopping) return;
                    if (result != 0) { Thread.Sleep(RetryDelay); continue; }

                    _onChanged();

                    // The notification is one-shot, so the loop re-arms. Re-opening also picks
                    // up a now-deeper existing ancestor, narrowing the watch as keys appear.
                }
                catch (Exception)
                {
                    if (!_stopping) Thread.Sleep(RetryDelay);
                }
                finally
                {
                    _watchedKey = null;
                    if (key != null) key.Dispose();
                }
            }
        }

        /// <summary>
        /// Opens the exact key when it exists — the quietest watch, no subtree. Otherwise the
        /// nearest existing parent WITH subtree notification, so the key's creation is seen.
        /// </summary>
        private RegistryKey? OpenDeepestExistingAncestor(out bool watchSubtree)
        {
            string path = _keyPath;
            watchSubtree = false;

            while (true)
            {
                RegistryKey? key = Registry.CurrentUser.OpenSubKey(path);
                if (key != null) return key;

                int cut = path.LastIndexOf('\\');
                if (cut < 0) return Registry.CurrentUser.OpenSubKey("Software");

                path = path.Substring(0, cut);
                watchSubtree = true; // an ancestor watch must see the subtree to catch creation
            }
        }

        public void Dispose()
        {
            _stopping = true;

            // Closing the watched key releases the blocked RegNotifyChangeKeyValue wait, which
            // is the only way to wake that thread.
            try { _watchedKey?.Dispose(); }
            catch (Exception) { /* already closed by the loop's finally */ }
            _watchedKey = null;
        }

        [DllImport("advapi32.dll")]
        private static extern int RegNotifyChangeKeyValue(
            SafeRegistryHandle key, bool watchSubtree, uint notifyFilter,
            IntPtr eventHandle, bool asynchronous);
    }
}
