using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.Service
{
    /// <summary>
    /// Tells a console instance of the service (dev, portable copy - anything not installed as a
    /// Windows service) that this process is using it, so it lives exactly as long as some
    /// program does (the service side is Http/OwnerWatcher):
    ///   • on the first window: <c>registerOwner</c> with this process id;
    ///   • every 30 s while windows are open: <c>registerOwner</c> again - a service that
    ///     restarted (a database-path change) or was started by another program learns who is
    ///     still using it;
    ///   • when the last window closes: <c>releaseOwner</c>, and the service exits at once if no
    ///     other program is registered. A program that crashes never says goodbye; the service
    ///     notices on its own poll.
    /// The installed Windows service accepts and ignores both ops.
    ///
    /// Process-wide and reference-counted: the Word add-in can have several task panes.
    /// Best effort throughout - ownership never blocks or fails the app.
    /// </summary>
    public static class KitveiHakodeshServiceOwnership
    {
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
        private static readonly object Sync = new object();
        private static int _windowCount;
        private static Timer _heartbeat;
        private static IKitveiHakodeshServiceChannel _channel;

        /// <summary>A window that uses the service opened. <paramref name="channel"/> should NOT
        /// start the service itself: a heartbeat must not revive a service nobody is using.</summary>
        public static void WindowOpened(IKitveiHakodeshServiceChannel channel)
        {
            lock (Sync)
            {
                _channel = channel;
                if (++_windowCount > 1) return;
                _heartbeat = new Timer(_ => Send("registerOwner"), null, TimeSpan.Zero, HeartbeatInterval);
            }
        }

        /// <summary>Registers right away (after the service was just brought up) instead of
        /// waiting for the next heartbeat.</summary>
        public static void RegisterNow() => Send("registerOwner");

        /// <summary>A window closed; the last one stops the heartbeat and says goodbye.</summary>
        public static void WindowClosed()
        {
            lock (Sync)
            {
                if (_windowCount == 0 || --_windowCount > 0) return;
                _heartbeat?.Dispose();
                _heartbeat = null;
            }
            // Synchronous, bounded: this runs while the window is closing.
            try { Task.Run(() => SendAsync("releaseOwner")).Wait(TimeSpan.FromSeconds(2)); }
            catch { /* the service's own poll notices */ }
        }

        private static void Send(string op)
        {
            _ = SendAsync(op);
        }

        private static async Task SendAsync(string op)
        {
            IKitveiHakodeshServiceChannel channel = _channel;
            if (channel == null) return;
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    await channel.CallAsync(OwnerRequest(op, Process.GetCurrentProcess().Id), timeout.Token).ConfigureAwait(false);
            }
            catch { /* not running right now - nothing to tell */ }
        }

        /// <summary>
        /// The MessagePack request <c>{ Op: op, Args: msgpack({ Pid: pid }) }</c>, written by
        /// hand because this library carries no MessagePack package. Key names and nesting
        /// match the service's RpcRequest / OwnerArgs (keyAsPropertyName); Args is a nested
        /// msgpack blob (bin8), exactly as serviceClient.ts sends it.
        /// </summary>
        private static byte[] OwnerRequest(string op, int pid)
        {
            // Args: fixmap(1) { fixstr "Pid": int32 }
            byte[] args =
            {
                0x81, 0xA3, (byte)'P', (byte)'i', (byte)'d',
                0xD2, (byte)(pid >> 24), (byte)(pid >> 16), (byte)(pid >> 8), (byte)pid,
            };
            byte[] opBytes = Encoding.ASCII.GetBytes(op); // < 32 bytes: fixstr

            var request = new byte[1 + 3 + 1 + opBytes.Length + 5 + 2 + args.Length];
            int i = 0;
            request[i++] = 0x82;                                               // fixmap(2)
            request[i++] = 0xA2; request[i++] = (byte)'O'; request[i++] = (byte)'p';
            request[i++] = (byte)(0xA0 | opBytes.Length);
            Buffer.BlockCopy(opBytes, 0, request, i, opBytes.Length); i += opBytes.Length;
            request[i++] = 0xA4; request[i++] = (byte)'A'; request[i++] = (byte)'r';
            request[i++] = (byte)'g'; request[i++] = (byte)'s';
            request[i++] = 0xC4; request[i++] = (byte)args.Length;             // bin8
            Buffer.BlockCopy(args, 0, request, i, args.Length);
            return request;
        }
    }
}
