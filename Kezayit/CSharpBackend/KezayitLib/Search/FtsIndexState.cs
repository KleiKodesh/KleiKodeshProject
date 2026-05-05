using FtsLib.Seforim;
using Microsoft.Win32;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KezayitLib.Search
{
    internal sealed class FtsIndexState
    {
        internal enum State { Idle, Building, Ready, Merging }

        internal readonly object Lock = new object();

        internal State CurrentState = State.Idle;

        internal bool IsReady    => CurrentState == State.Ready || CurrentState == State.Merging;
        internal bool IsIndexing => CurrentState == State.Building;

        internal string       DbPath;
        internal SeforimIndex Index;

        internal Task                    IndexingTask;
        internal Task                    MergeTask;
        internal CancellationTokenSource IndexingCts;

        // ── Paths ─────────────────────────────────────────────────────────────────

        internal static string FtsIndexPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FtsIndex");

        internal static string FtsVersionStampPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FtsIndex", "fts.ver");

        internal static string BloomFolderPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters");

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Cancels any running build, waits for it and any running merge to fully stop,
        /// then resets state to Idle. Safe to call from any thread.
        /// After this returns, no background work is touching the index directory.
        /// </summary>
        internal void StopAll()
        {
            Task indexingTask, mergeTask;
            CancellationTokenSource cts;
            lock (Lock)
            {
                cts          = IndexingCts;
                indexingTask = IndexingTask;
                mergeTask    = MergeTask;
            }

            cts?.Cancel();

            if (indexingTask != null)
            {
                try { indexingTask.Wait(15000); } catch { }
            }
            if (mergeTask != null)
            {
                try { mergeTask.Wait(30000); } catch { }
            }

            lock (Lock)
            {
                CurrentState = State.Idle;
                IndexingTask = null;
                MergeTask    = null;
                IndexingCts  = null;
            }
        }

        // ── Index directory ───────────────────────────────────────────────────────

        internal static void DeleteFtsIndex()
        {
            try
            {
                if (Directory.Exists(FtsIndexPath))
                {
                    Directory.Delete(FtsIndexPath, recursive: true);
                    Console.WriteLine("[SearchHandler] Deleted FTS index directory");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SearchHandler] Failed to delete FTS index: " + ex.Message);
            }
        }

        internal static void DeleteBloomIndexIfPresent()
        {
            try
            {
                if (Directory.Exists(BloomFolderPath))
                {
                    Directory.Delete(BloomFolderPath, recursive: true);
                    Console.WriteLine("[SearchHandler] Deleted legacy Bloom index folder");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SearchHandler] Failed to delete Bloom folder: " + ex.Message);
            }
        }

        // ── Validation ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns null if the FTS index directory looks valid (exists and contains
        /// segment files), or a reason string if it is missing or empty.
        /// </summary>
        internal static string ValidateFtsIndex()
        {
            try
            {
                if (!Directory.Exists(FtsIndexPath)) return "index directory missing";
                if (Directory.GetFiles(FtsIndexPath, "*.dat").Length == 0)
                    return "no segment files found";
                return null;
            }
            catch (Exception ex) { return "validation error: " + ex.Message; }
        }

        /// <summary>
        /// Returns true when the index has more than one segment .dat file, meaning
        /// the background merge either never ran or was interrupted and should resume.
        /// A fully merged index has exactly one seg_*.dat file.
        /// </summary>
        internal static bool MergeNeeded()
        {
            try
            {
                if (!Directory.Exists(FtsIndexPath)) return false;
                return Directory.GetFiles(FtsIndexPath, "seg_*.dat").Length > 1;
            }
            catch { return false; }
        }

        // ── Version stamp ─────────────────────────────────────────────────────────

        internal static string GetInstalledAppVersion()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\KleiKodesh"))
                    return key?.GetValue("Version")?.ToString();
            }
            catch { return null; }
        }

        internal static string ReadVersionStamp()
        {
            try
            {
                return File.Exists(FtsVersionStampPath)
                    ? File.ReadAllText(FtsVersionStampPath).Trim()
                    : null;
            }
            catch { return null; }
        }

        internal static void WriteVersionStamp(string version)
        {
            try
            {
                Directory.CreateDirectory(FtsIndexPath);
                File.WriteAllText(FtsVersionStampPath, version ?? "");
            }
            catch { }
        }
    }
}
