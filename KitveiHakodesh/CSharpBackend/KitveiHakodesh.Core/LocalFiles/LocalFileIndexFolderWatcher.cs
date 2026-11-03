// Same convention as LocalFileIndex, whose private writer this feeds.
#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace KitveiHakodesh.Core.LocalFiles
{
    /// <summary>
    /// Keeps one drive of the index current when the USN journal cannot be read (process not
    /// elevated, or a non-NTFS drive). Mirrors Everything's "Folder Indexing" (voidtools docs,
    /// support/everything/folder_indexing): it "monitors indexed folders for changes" with no
    /// admin rights, uses a 64 KB change buffer (their default), rescans the folder when that
    /// buffer fills ("Rescan on full buffer"), and leaves the daily rescan
    /// (<see cref="LocalFileIndex.StartPeriodicRescan"/>) as the net for anything missed.
    ///
    /// One <see cref="FileSystemWatcher"/> on the drive root, recursive. Events are queued and
    /// applied in batches on one worker thread, so a burst becomes one index commit and a
    /// rescan never races a batch.
    /// </summary>
    internal sealed class LocalFileIndexFolderWatcher : IDisposable
    {
        public enum ChangeKind { Upserted, Removed }

        public readonly struct Change
        {
            public readonly ChangeKind Kind;
            public readonly string Path;
            public Change(ChangeKind kind, string path) { Kind = kind; Path = path; }
        }

        /// <summary>Everything's default change-buffer size, and the most Windows allows
        /// for a watcher on a network drive.</summary>
        private const int BufferSize = 64 * 1024;

        /// <summary>How long to gather events after the first one before applying them, so a
        /// burst (an unzip, a copy) is one commit instead of thousands.</summary>
        private static readonly TimeSpan BatchWindow = TimeSpan.FromMilliseconds(200);

        private readonly char _driveLetter;
        private readonly Action<List<Change>, CancellationToken> _apply;
        private readonly Action<char, CancellationToken> _rescan;
        private readonly Action<string, Exception> _onError;
        private readonly CancellationToken _ct;
        private readonly FileSystemWatcher _watcher;
        private readonly Thread _worker;

        private readonly object _queueLock = new object();
        private List<Change> _queue = new List<Change>();
        private bool _rescanPending;
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);

        public LocalFileIndexFolderWatcher(
            char driveLetter,
            Action<List<Change>, CancellationToken> apply,
            Action<char, CancellationToken> rescan,
            CancellationToken ct,
            Action<string, Exception> onError)
        {
            _driveLetter = char.ToUpper(driveLetter);
            _apply = apply;
            _rescan = rescan;
            _onError = onError;
            _ct = ct;

            _watcher = new FileSystemWatcher(_driveLetter + ":\\")
            {
                IncludeSubdirectories = true,
                InternalBufferSize = BufferSize,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            _watcher.Created += (_, e) => Enqueue(ChangeKind.Upserted, e.FullPath);
            // Changed fires for a folder too whenever something inside it changes; that child
            // raises its own event, so only files of an indexed type are worth a re-stamp.
            _watcher.Changed += (_, e) =>
            {
                if (LocalFileIndexMftCrawler.IsAllowedExtension(e.Name ?? "") && File.Exists(e.FullPath))
                    Enqueue(ChangeKind.Upserted, e.FullPath);
            };
            _watcher.Deleted += (_, e) => Enqueue(ChangeKind.Removed, e.FullPath);
            _watcher.Renamed += (_, e) =>
            {
                Enqueue(ChangeKind.Removed, e.OldFullPath);
                Enqueue(ChangeKind.Upserted, e.FullPath);
            };
            _watcher.Error += (_, e) => OnWatcherError(e.GetException());

            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = $"pathindex-folderwatch-{_driveLetter}",
            };
            _worker.Start();
            _watcher.EnableRaisingEvents = true;
        }

        private void Enqueue(ChangeKind kind, string path)
        {
            // Skipped system folders never enter the index, so their churn is dropped here
            // (the extension test needs to know file from folder, so it runs at apply time).
            // The dummy last segment makes the path's own name count too: a folder created
            // or renamed INTO a skipped name must not be crawled.
            if (LocalFileIndexMftCrawler.IsInSkippedDirectory(path + "\\_")) return;
            lock (_queueLock) _queue.Add(new Change(kind, path));
            try { _signal.Set(); } catch (ObjectDisposedException) { /* disposed */ }
        }

        private void OnWatcherError(Exception ex)
        {
            // Buffer overflow: changes were lost, so rescan the drive - Everything's
            // "Rescan on full buffer". Any other error (the watcher itself died) loses events
            // the same way, and is handled the same way after restarting the watcher.
            if (!(ex is InternalBufferOverflowException))
            {
                _onError?.Invoke($"Folder watcher on {_driveLetter}: failed, restarting", ex);
                try
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.EnableRaisingEvents = true;
                }
                catch (Exception restartEx)
                {
                    _onError?.Invoke($"Folder watcher on {_driveLetter}: restart failed", restartEx);
                }
            }
            lock (_queueLock) _rescanPending = true;
            try { _signal.Set(); } catch (ObjectDisposedException) { /* disposed */ }
        }

        private void WorkerLoop()
        {
            var handles = new WaitHandle[] { _signal, _ct.WaitHandle };
            while (!_ct.IsCancellationRequested)
            {
                try
                {
                    if (WaitHandle.WaitAny(handles) == 1) return;
                    if (_ct.WaitHandle.WaitOne(BatchWindow)) return;

                    List<Change> batch;
                    bool rescan;
                    lock (_queueLock)
                    {
                        batch = _queue;
                        _queue = new List<Change>();
                        rescan = _rescanPending;
                        _rescanPending = false;
                    }

                    // A rescan supersedes every queued change: it reads the drive as it is now.
                    if (rescan) _rescan(_driveLetter, _ct);
                    else if (batch.Count > 0) _apply(batch, _ct);
                }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException) { return; } // index disposed under us
                catch (Exception ex)
                {
                    if (_ct.IsCancellationRequested) return;
                    _onError?.Invoke($"Folder watcher on {_driveLetter}: applying changes failed", ex);
                }
            }
        }

        public void Dispose()
        {
            try { _watcher.EnableRaisingEvents = false; } catch { /* best effort */ }
            _watcher.Dispose();
            // The worker wakes on the cancelled token (the index cancels it before disposing).
            if (_worker != Thread.CurrentThread) _worker.Join(TimeSpan.FromSeconds(2));
            _signal.Dispose();
        }
    }
}
