// Written before nullable annotations (ported from the DocumentLocator library, which built
// with these warnings suppressed); not annotated yet.
#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace KitveiHakodesh.Core.LocalFiles
{
    /// <summary>
    /// Recursive directory-walk crawler used when the MFT fast path is unavailable —
    /// the process is not elevated, the volume handle is refused, the volume is not
    /// NTFS, or the MFT crawl unexpectedly produces no paths.
    ///
    /// Indexes exactly the same file types (<see cref="LocalFileIndexMftCrawler.IsAllowedExtension"/>)
    /// and skips exactly the same system folders (<see cref="LocalFileIndexMftCrawler.IsSkippedDirectoryName"/>)
    /// as the MFT crawl, so an index built through either strategy is equivalent.
    ///
    /// Slower than the MFT crawl (one directory listing per folder instead of one
    /// linear MFT pass) but requires no special privileges and works on any file system.
    /// </summary>
    public static class LocalFileIndexDirectoryCrawler
    {
        // ── Combined entry point ──────────────────────────────────────────────────

        /// <summary>
        /// Enumerates all indexable paths on a drive: tries the fast MFT crawl first;
        /// if it throws (blocked) or yields nothing, re-crawls with the recursive
        /// directory walk. Paths are returned lower-cased, matching
        /// <see cref="LocalFileIndexMftCrawler.EnumeratePaths"/>.
        /// </summary>
        public static List<string> EnumeratePathsWithFallback(
            char              driveLetter,
            Action<string>    status   = null,
            Action<int>       progress = null,
            CancellationToken ct       = default)
        {
            char drive = char.ToUpper(driveLetter);
            List<string> paths = null;

            try
            {
                var collected = new List<string>();
                foreach (var path in LocalFileIndexMftCrawler.EnumeratePaths(driveLetter, progress, ct))
                    collected.Add(path);
                paths = collected;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                status?.Invoke(
                    $"{drive}: MFT crawl blocked ({ex.Message}) — falling back to directory walk");
            }

            if (paths != null && paths.Count > 0)
                return paths;

            if (paths != null)
                status?.Invoke(
                    $"{drive}: MFT crawl returned no paths — falling back to directory walk");

            var fallback = new List<string>();
            foreach (var path in EnumeratePaths(driveLetter, progress, ct))
                fallback.Add(path);
            return fallback;
        }

        // ── Recursive directory walk ──────────────────────────────────────────────

        /// <summary>
        /// Enumerates indexable paths on a drive by walking the directory tree from
        /// the root. Uses an explicit stack (depth-first) so arbitrarily deep trees
        /// cannot overflow the call stack. Inaccessible folders are skipped silently;
        /// reparse points (junctions, symlinks) are not followed, preventing cycles.
        /// Paths are yielded lower-cased.
        /// </summary>
        public static IEnumerable<string> EnumeratePaths(
            char              driveLetter,
            Action<int>       progress = null,
            CancellationToken ct       = default)
        {
            string root = char.ToUpper(driveLetter) + ":\\";
            int count = 0;
            int reportThreshold = 1_000;

            var pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                string dir = pending.Pop();

                // A single listing per folder gives names and attributes together
                // (populated from the find data — no extra syscall per entry).
                FileSystemInfo[] children = null;
                try
                {
                    children = new DirectoryInfo(dir).GetFileSystemInfos();
                }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.SecurityException) { }
                catch (DirectoryNotFoundException) { }
                catch (IOException) { }
                if (children == null) continue;

                foreach (var child in children)
                {
                    ct.ThrowIfCancellationRequested();

                    if ((child.Attributes & FileAttributes.Directory) != 0)
                    {
                        if ((child.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if (LocalFileIndexMftCrawler.IsSkippedDirectoryName(child.Name)) continue;
                        pending.Push(child.FullName);
                    }
                    else if (LocalFileIndexMftCrawler.IsAllowedExtension(child.Name))
                    {
                        count++;
                        if (count >= reportThreshold)
                        {
                            progress?.Invoke(count);
                            reportThreshold = count + 1_000;
                        }
                        yield return child.FullName.ToLowerInvariant();
                    }
                }
            }

            progress?.Invoke(count);
        }

        // ── Drive discovery ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns all ready fixed drives regardless of file system. Used when
        /// <see cref="LocalFileIndexMftCrawler.GetNtfsDrives"/> finds nothing (e.g. exotic file
        /// systems, or drive metadata unavailable) so indexing can still proceed
        /// through the directory walk.
        /// </summary>
        public static List<DriveInfo> GetFallbackDrives()
        {
            var list = new List<DriveInfo>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.IsReady && d.DriveType == DriveType.Fixed)
                        list.Add(d);
                }
                catch { }   // drive metadata can throw for detached/locked volumes
            }
            return list;
        }
    }
}
