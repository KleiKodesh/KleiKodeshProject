using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>
    /// Registers an application as an "Open With" handler for the file types it can open.
    ///
    /// Everything is written under HKCU\Software\Classes, so no elevation is needed and nothing
    /// is done to other users of the machine. That is not a convenience — a per-user install
    /// cannot write HKLM, and asking for admin rights to add a context-menu entry would be a
    /// bad trade even if it could.
    ///
    /// The layout, for progId "X.Document.1" and executable "app.exe":
    ///
    ///   HKCU\Software\Classes\X.Document.1
    ///     (Default)                = friendly name
    ///     shell\open\command       = "\"&lt;exe&gt;\" \"%1\""
    ///
    ///   HKCU\Software\Classes\Applications\app.exe
    ///     FriendlyAppName          = friendly name
    ///     SupportedTypes\.pdf …    = (empty REG_SZ per extension)
    ///     shell\open\command       = "\"&lt;exe&gt;\" \"%1\""
    ///
    ///   HKCU\Software\Classes\&lt;.ext&gt;\OpenWithProgids
    ///     X.Document.1             = (empty REG_BINARY)
    ///
    /// Both halves are needed: the Applications entry is what Explorer's "Open with" list reads,
    /// and the per-extension OpenWithProgids is what makes the app appear for that extension
    /// specifically.
    ///
    /// The progId, the friendly name and the extension list are the CALLER's — this class holds
    /// no app identity of its own, and the friendly name is text a user reads, which Core does
    /// not author.
    /// </summary>
    public sealed class ShellRegistration
    {
        private const int AssociationsChanged = 0x08000000;   // SHCNE_ASSOCCHANGED
        private const uint NotifyByIdList = 0x0000;           // SHCNF_IDLIST

        private const string ClassesRoot = @"Software\Classes";

        private readonly string _progId;
        private readonly string _friendlyName;
        private readonly IReadOnlyList<string> _extensions;

        /// <param name="progId">The programmatic identifier, conventionally
        /// "Company.Type.Version" — e.g. "KitveiHakodesh.Document.1".</param>
        /// <param name="friendlyName">The name Explorer shows. The caller's to write; it is
        /// user-facing text.</param>
        /// <param name="extensions">Extensions to handle, each with its leading dot.</param>
        public ShellRegistration(string progId, string friendlyName, IReadOnlyList<string> extensions)
        {
            if (string.IsNullOrWhiteSpace(progId))
                throw new ArgumentException("progId is required", nameof(progId));
            if (string.IsNullOrWhiteSpace(friendlyName))
                throw new ArgumentException("friendlyName is required", nameof(friendlyName));
            if (extensions == null || extensions.Count == 0)
                throw new ArgumentException("at least one extension is required", nameof(extensions));

            _progId = progId;
            _friendlyName = friendlyName;
            _extensions = extensions;
        }

        /// <summary>
        /// Registers the handler. Idempotent — safe to call on every launch, which is how it
        /// survives the executable moving (a portable app's path changes between runs).
        /// </summary>
        public void Register(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException("executablePath is required", nameof(executablePath));

            string exeFileName = Path.GetFileName(executablePath);
            string command = "\"" + executablePath + "\" \"%1\"";

            using (RegistryKey classes =
                Registry.CurrentUser.OpenSubKey(ClassesRoot, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(ClassesRoot))
            {
                RegisterProgId(classes, command);
                RegisterApplicationEntry(classes, exeFileName, command);
                RegisterExtensions(classes);
            }

            NotifyShell();
        }

        /// <summary>
        /// Removes everything <see cref="Register"/> wrote. Safe when it was never called —
        /// a key that is not there is the desired end state, not a failure.
        /// </summary>
        public void Unregister(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException("executablePath is required", nameof(executablePath));

            string exeFileName = Path.GetFileName(executablePath);

            using (RegistryKey? classes = Registry.CurrentUser.OpenSubKey(ClassesRoot, writable: true))
            {
                if (classes == null) return;

                TryDeleteSubKeyTree(classes, _progId);
                TryDeleteSubKeyTree(classes, @"Applications\" + exeFileName);
                RemoveFromExtensions(classes);
            }

            NotifyShell();
        }

        private void RegisterProgId(RegistryKey classes, string command)
        {
            using RegistryKey progId = classes.CreateSubKey(_progId);
            progId.SetValue("", _friendlyName);

            using RegistryKey openCommand = progId.CreateSubKey(@"shell\open\command");
            openCommand.SetValue("", command);
        }

        private void RegisterApplicationEntry(RegistryKey classes, string exeFileName, string command)
        {
            using RegistryKey application = classes.CreateSubKey(@"Applications\" + exeFileName);
            application.SetValue("FriendlyAppName", _friendlyName);

            using (RegistryKey supportedTypes = application.CreateSubKey("SupportedTypes"))
            {
                foreach (string extension in _extensions)
                    supportedTypes.SetValue(extension, "");
            }

            using RegistryKey openCommand = application.CreateSubKey(@"shell\open\command");
            openCommand.SetValue("", command);
        }

        private void RegisterExtensions(RegistryKey classes)
        {
            foreach (string extension in _extensions)
            {
                using RegistryKey progIds = classes.CreateSubKey(extension + @"\OpenWithProgids");
                // REG_BINARY and empty: the VALUE NAME is the progId and the data is unused.
                // Explorer reads the names here, so writing anything into the data is noise.
                progIds.SetValue(_progId, new byte[0], RegistryValueKind.Binary);
            }
        }

        private void RemoveFromExtensions(RegistryKey classes)
        {
            foreach (string extension in _extensions)
            {
                using RegistryKey? progIds =
                    classes.OpenSubKey(extension + @"\OpenWithProgids", writable: true);
                if (progIds == null) continue;

                try { progIds.DeleteValue(_progId, throwOnMissingValue: false); }
                catch (Exception) { /* another handler's key, or a permission — leave it alone */ }
            }
        }

        private static void TryDeleteSubKeyTree(RegistryKey parent, string subKey)
        {
            try { parent.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false); }
            catch (Exception) { /* already gone, or in use — the end state is what matters */ }
        }

        /// <summary>
        /// Tells Explorer the associations changed, so the "Open with" list updates now instead
        /// of at the next sign-in. Best-effort: failing to notify costs freshness, not
        /// correctness, and the registration itself already succeeded.
        /// </summary>
        private static void NotifyShell()
        {
            try { SHChangeNotify(AssociationsChanged, NotifyByIdList, IntPtr.Zero, IntPtr.Zero); }
            catch (Exception) { /* the entries are written; Explorer catches up on its own */ }
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}
