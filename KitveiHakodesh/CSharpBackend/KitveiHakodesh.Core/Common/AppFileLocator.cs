using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
                KitveiHakodesh.Core.Settings.UserProfile.LocalAppData,
                "KleiKodesh");

        /// <summary>
        /// THE APP ROOT: the one folder everything the app writes lives under (subfolders only;
        /// the app never saves outside it). The folder that holds KitveiHakodeshService\ - the
        /// same answer for the host, the service, installed, portable and dev. The full rule,
        /// with KHS_APP_ROOT as the explicit override, is on <see cref="AppRootFolder"/>.
        /// </summary>
        public static string AppRoot => AppRootFolder.Path;

        /// <summary>
        /// The folder that holds the data kept "beside the exe" (FtsIndex, CatalogTocIndex,
        /// hb-cache, convert-cache, updates, Dictionary, HebrewBooks, SearchExpansion,
        /// filesystemindex ...). Normally AppContext.BaseDirectory — the installed service
        /// keeps its data next to its binary on purpose.
        ///
        /// KHS_DATA_DIR overrides it. The dev server runs the service from a SHADOW COPY of
        /// the build output (so a running service never locks bin\Release against another
        /// rebuild) and points this at the real build folder, where the ~1.5 GB of data stays.
        /// Absent the variable (installed, portable) nothing changes. Every "data beside the
        /// exe" path must go through here, never through AppContext.BaseDirectory directly.
        /// </summary>
        public static string DataRoot
        {
            get
            {
                string? env = Environment.GetEnvironmentVariable("KHS_DATA_DIR");
                return string.IsNullOrWhiteSpace(env) ? AppContext.BaseDirectory : env!;
            }
        }

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

        [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Empty Assembly.Location is filtered out; see IL3000 note below.")]
        private static IEnumerable<string?> EnumerateRootsRaw()
        {
            // 0. The data root — differs from (1) only when KHS_DATA_DIR is set (dev shadow
            //    copy); the same folder otherwise, deduplicated by CandidateRoots.
            yield return SafeDirectory(() => DataRoot);

            // 1. Next to the running assembly. Correct for the portable DemoApp and for the
            //    service; under VSTO this is the shadow-copy folder and simply will not match.
            yield return SafeDirectory(() => AppContext.BaseDirectory);

            // 2. This assembly's own folder — differs from (1) when the library is loaded
            //    from outside the entry application's directory.
            // 3. The entry executable's folder. This is WINWORD.EXE under the add-in, which
            //    is what today's code accidentally relies on — kept as a candidate, not a rule.
            //
            // IL3000: Assembly.Location returns "" in a single-file app. That is fine here and
            // is the reason this is a PROBE rather than a lookup — an empty or wrong answer is
            // dropped by the IsNullOrWhiteSpace filter in CandidateRoots and the next candidate
            // is tried. Suppressed rather than removed because these two are the only roots
            // that resolve when the library is loaded from outside the entry app's folder.
            yield return SafeDirectory(() =>
                Path.GetDirectoryName(typeof(AppFileLocator).Assembly.Location));

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
        /// Where to WRITE <paramref name="relativePath"/>: always inside the app folder
        /// (<see cref="AppRoot"/>), never anywhere else (user, 2026-11-08 - the portable app
        /// carries everything with it). When the app folder is read-only (a locked USB stick, a
        /// network share) nothing is saved: the write fails at its first attempt and the caller's
        /// own error path handles it. There is deliberately no fallback outside the app folder.
        /// </summary>
        public static string ResolveWritablePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("relativePath is required", nameof(relativePath));
            return Path.Combine(AppRoot, relativePath);
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
