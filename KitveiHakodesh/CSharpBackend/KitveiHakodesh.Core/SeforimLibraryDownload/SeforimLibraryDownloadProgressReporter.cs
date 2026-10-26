using System;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>
    /// Holds the current <see cref="SeforimLibraryDownloadProgress"/> and tells listeners when it
    /// moves: every phase change at once, byte counts at most every 250 ms, so a 2 GB copy
    /// cannot flood the apps with a frame per buffer.
    /// </summary>
    public sealed class SeforimLibraryDownloadProgressReporter
    {
        private static readonly long ThrottleMilliseconds = 250;

        private readonly object _gate = new object();
        private readonly SeforimLibraryDownloadProgress _progress = new SeforimLibraryDownloadProgress();
        private long _lastNotifyTicks;

        /// <summary>Raised with a fresh snapshot. Handlers must be quick.</summary>
        public event Action? Changed;

        /// <summary>A copy of the current state; <see cref="SeforimLibraryDownloadProgress.Pending"/>
        /// and <see cref="SeforimLibraryDownloadProgress.Running"/> are the caller's to fill.</summary>
        public SeforimLibraryDownloadProgress Snapshot()
        {
            lock (_gate)
            {
                return new SeforimLibraryDownloadProgress
                {
                    Phase = _progress.Phase,
                    Received = _progress.Received,
                    Total = _progress.Total,
                    ReleaseTag = _progress.ReleaseTag,
                    Failure = _progress.Failure,
                    Detail = _progress.Detail,
                };
            }
        }

        public void SetReleaseTag(string tag)
        {
            lock (_gate) _progress.ReleaseTag = tag;
        }

        /// <summary>Starts a phase, reported at once.</summary>
        public void Phase(SeforimLibraryDownloadPhase phase, long total = 0) => Report(phase, 0, total, force: true);

        /// <summary>A progress callback for the given phase, throttled.</summary>
        public Action<long, long> BytesOf(SeforimLibraryDownloadPhase phase) =>
            (received, total) => Report(phase, received, total, force: false);

        public void Fail(SeforimLibraryDownloadFailure failure, string? detail)
        {
            lock (_gate)
            {
                _progress.Phase = SeforimLibraryDownloadPhase.Failed;
                _progress.Failure = failure;
                _progress.Detail = detail;
            }
            Notify();
        }

        public void Notify() => Changed?.Invoke();

        private void Report(SeforimLibraryDownloadPhase phase, long received, long total, bool force)
        {
            lock (_gate)
            {
                bool phaseChanged = _progress.Phase != phase;
                _progress.Phase = phase;
                _progress.Received = received;
                _progress.Total = total;
                _progress.Failure = SeforimLibraryDownloadFailure.None;
                _progress.Detail = null;

                long now = Environment.TickCount64;
                if (!force && !phaseChanged && now - _lastNotifyTicks < ThrottleMilliseconds) return;
                _lastNotifyTicks = now;
            }
            Notify();
        }
    }
}
