using System.Collections.Concurrent;
using System.Text.Json;

namespace KitveiHakodeshService.Server;

/// <summary>
/// Manages all open Server-Sent Events connections and broadcasts push events
/// to every connected client.
///
/// Each client that connects to GET /events gets its own Channel&lt;string&gt;.
/// PushEvent serialises the payload to JSON and writes it to every channel.
/// Disconnected clients are removed lazily on the next write attempt.
///
/// This is the server-side equivalent of WebBridge.PushEvent() in KitveiHakodeshLib.
/// </summary>
public sealed class SseManager
{
    private readonly ConcurrentDictionary<string, System.Threading.Channels.Channel<string>> _clients
        = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Registers a new SSE client and returns a reader the endpoint can drain.
    /// The caller is responsible for calling Remove(connectionId) when the client disconnects.
    /// </summary>
    public (string connectionId, System.Threading.Channels.ChannelReader<string> reader) AddClient()
    {
        var connectionId = Guid.NewGuid().ToString("N");
        // Bounded channel — if the client is too slow to consume events, old ones are
        // dropped rather than allowing unbounded memory growth.
        var channel = System.Threading.Channels.Channel.CreateBounded<string>(
            new System.Threading.Channels.BoundedChannelOptions(256)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });
        _clients[connectionId] = channel;
        return (connectionId, channel.Reader);
    }

    /// <summary>Removes a disconnected client.</summary>
    public void RemoveClient(string connectionId)
    {
        _clients.TryRemove(connectionId, out _);
    }

    /// <summary>
    /// Serialises the payload and writes an SSE message to every connected client.
    /// Dead clients (completed channels) are removed.
    /// </summary>
    public void PushEvent(object payload)
    {
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        // SSE format: "data: {json}\n\n"
        string message = $"data: {json}\n\n";

        foreach (var (id, channel) in _clients)
        {
            if (!channel.Writer.TryWrite(message))
            {
                // Channel is complete (client disconnected) or full.
                // The bounded channel handles full via DropOldest, so TryWrite only
                // returns false when the channel is completed — clean it up.
                if (channel.Reader.Completion.IsCompleted)
                    _clients.TryRemove(id, out _);
            }
        }
    }

    /// <summary>Returns the number of currently connected SSE clients.</summary>
    public int ClientCount => _clients.Count;
}
