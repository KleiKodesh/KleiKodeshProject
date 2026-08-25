using System;
using System.Collections.Generic;
using System.IO;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>
    /// Finds a file or folder that ships with, or is written by, the app — without the
    /// caller having to know which host it is running in.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// There is no single expression that answers "where am I" correctly in all three hosts:
    ///
    ///   KitveiHakodeshService  installed per-user; data sits next to the binary on purpose
    ///                          (delete the service folder and its index goes with it).
    ///   VSTO Word add-in       assemblies are shadow-copied to %TEMP%\VSTO\..., so
    ///                          Assembly.Location is a temp folder holding no data, and
    ///                          AppDomain.BaseDirectory is WINWORD.EXE's folder — Office's
    ///                          install directory, which is not ours either.
    ///   DemoApp (PORTABLE)     runs from anywhere — a USB stick, a network share, a locked
    ///                          folder. Data should travel beside the exe, but that folder is
    ///                          not guaranteed writable and its path changes between runs.
    ///
    /// So instead of one rule, probe the candidates in order and take the first that actually
    /// EXISTS. Probing costs a few File.Exists calls once, and it is the only approach that is
    /// correct in all three hosts without anyone passing paths in.
    ///
    /// The last resort is the installer's own per-user root, %LocalAppData%\KleiKodesh
    /// (Build/Installer AddinInstaller.InstallPath) — kept in step with it deliberately.
    /// </summary>
    public static class AppFileLocator
    {
        /// <summary>The installer's per-user root: %LocalAppData%\KleiKodesh.
        /// Mirrors AddinInstaller.InstallPath — change both together.</summary>
        public static string UserInstallRoot =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KleiKodesh");

        /// <summary>
        /// Every root worth probing, most-specific first. Duplicates and blanks are dropped so
        /// a caller can enumerate without caring which host it is in.
        /// </summary>
        public static IEnumerable<string> CandidateRoots()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string? root in EnumerateRootsRaw())
            {
                if (string.IsNullOrWhiteSpace(root)) continue;

                string full;
                try { full = Path.GetFullPath(root!); }
                catch { continue; }          // malformed path — not worth failing over

                if (seen.Add(full)) yield return full;
            }
        }

        private static IEnumerable<string?> EnumerateRootsRaw()
        {
            // 1. Next to the running assembly. Correct for the portable DemoApp and for the
            //    service; under VSTO this is the shadow-copy folder and simply will not match.
            yield return SafeDirectory(() => AppContext.BaseDirectory);

            // 2. This assembly's own folder — differs from (1) when the library is loaded
            //    from outside the entry application's directory.
            yield return SafeDirectory(() =>
                Path.GetDirectoryName(typeof(AppFileLocator).Assembly.Location));

            // 3. The entry executable's folder. This is WINWORD.EXE under the add-in, which
            //    is what today's code accidentally relies on — kept as a candidate, not a rule.
            yield return SafeDirectory(() =>
            {
                var entry = System.Reflection.Assembly.GetEntryAssembly();
                return entry == null ? null : Path.GetDirectoryName(entry.Location);
            });

            // 4. The working directory — right when launched from a shortcut whose "Start in"
            //    points at the app, wrong often enough not to rank higher.
            yield return SafeDirectory(Directory.GetCurrentDirectory);

            // 5. The installer's per-user root: the dependable answer on an installed machine,
            //    and the only writable one when the app sits on read-only media.
            yield return UserInstallRoot;
        }

        private static string? SafeDirectory(Func<string?> get)
        {
            try { return get(); } catch { return null; }
        }

        /// <summary>
        /// First existing file matching <paramref name="relativePath"/> under any candidate
        /// root, or null when none has it. Never throws.
        /// </summary>
        public static string? FindFile(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;

            foreach (string root in CandidateRoots())
            {
                string candidate;
                try { candidate = Path.Combine(root, relativePath); }
                catch { continue; }

                try { if (File.Exists(candidate)) return candidate; }
                catch { /* unreachable root, e.g. disconnected drive — try the next */ }
            }
            return null;
        }

        /// <summary>First existing directory matching <paramref name="relativePath"/>, or null.</summary>
        public static string? FindDirectory(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;

            foreach (string root in CandidateRoots())
            {
                string candidate;
                try { candidate = Path.Combine(root, relativePath); }
                catch { continue; }

                try { if (Directory.Exists(candidate)) return candidate; }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Where to WRITE <paramref name="relativePath"/> — a different question from where to
        /// read it. Prefers a root that already holds the item, so an existing index is updated
        /// in place instead of a second one appearing elsewhere; otherwise the first root that
        /// is genuinely writable; finally the per-user install root.
        ///
        /// Writability is TESTED, not assumed: a portable app may be running from a USB stick,
        /// a network share or a read-only folder, and only trying reveals which.
        /// </summary>
        public static string ResolveWritablePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("relativePath is required", nameof(relativePath));

            string? existing = FindFile(relativePath) ?? FindDirectory(relativePath);
            if (existing != null && IsWritable(Path.GetDirectoryName(existing)))
                return existing;

            foreach (string root in CandidateRoots())
            {
                string candidate;
                try { candidate = Path.Combine(root, relativePath); }
                catch { continue; }

                if (IsWritable(Path.GetDirectoryName(candidate))) return candidate;
            }

            // Everything else was read-only or unreachable. %LocalAppData% is ours by
            // definition; let a failure surface here rather than at first write.
            string fallback = Path.Combine(UserInstallRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fallback)!);
            return fallback;
        }

        /// <summary>
        /// True when a file can actually be created in <paramref name="directory"/>; creates the
        /// directory if missing. Probes with a real temp file, because ACLs, read-only media and
        /// network shares all look fine until you try to write.
        /// </summary>
        public static bool IsWritable(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return false;

            try
            {
                Directory.CreateDirectory(directory!);
                string probe = Path.Combine(directory!, ".write-probe-" + Guid.NewGuid().ToString("N"));
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write,
                                      FileShare.None, 1, FileOptions.DeleteOnClose))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
