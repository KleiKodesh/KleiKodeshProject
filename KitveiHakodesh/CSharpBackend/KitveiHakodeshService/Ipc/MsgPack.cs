using MessagePack;
using MessagePack.Resolvers;

namespace KitveiHakodeshService.Ipc;

/// <summary>
/// Source-generated MessagePack resolver for every RPC DTO (AOT-safe — no reflection).
/// The generator emits a formatter for each [MessagePackObject] type; StandardResolver
/// supplies the AOT-safe primitive/collection formatters. This is the compact binary
/// replacement for System.Text.Json on the dev↔service channel — smaller payloads and
/// faster encode/decode than JSON, which matters most for the large FTS result sets.
/// Inherently-dynamic / already-JSON data (the raw-SQL user-settings params + rows) is
/// NOT re-encoded — it rides as JSON strings inside the msgpack envelope (see RawSqlArgs).
/// </summary>
[GeneratedMessagePackResolver]
internal partial class KhsMsgPackResolver;

internal static class MsgPack
{
    public static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(
            CompositeResolver.Create(KhsMsgPackResolver.Instance, StandardResolver.Instance));

    /// <summary>Serialize a DTO to MessagePack bytes.</summary>
    public static byte[] Ser<T>(T value) => MessagePackSerializer.Serialize(value, Options);

    /// <summary>Deserialize a DTO from MessagePack bytes (null/empty → default new()).</summary>
    public static T De<T>(byte[]? bytes) where T : new()
        => bytes is null || bytes.Length == 0
            ? new T()
            : MessagePackSerializer.Deserialize<T>(bytes, Options) ?? new T();
}

/// <summary>Response envelope: { ok, result?, error? }. <c>Result</c> is the msgpack bytes
/// of the op's result DTO (nested bin) so the envelope needs no generic type — AOT-friendly.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed class RpcEnvelope
{
    public bool Ok { get; set; }
    public byte[]? Result { get; set; }
    public string? Error { get; set; }
}

// ── Tiny status-flag result DTOs (preserve the current response shapes) ──────────
[MessagePackObject(keyAsPropertyName: true)] public sealed class PongResult { public bool Pong { get; set; } = true; }
[MessagePackObject(keyAsPropertyName: true)] public sealed class ShuttingDownResult { public bool ShuttingDown { get; set; } = true; }
[MessagePackObject(keyAsPropertyName: true)] public sealed class StartedResult { public bool Started { get; set; } = true; }
[MessagePackObject(keyAsPropertyName: true)] public sealed class ResetResult { public bool Reset { get; set; } = true; }
[MessagePackObject(keyAsPropertyName: true)] public sealed class CancelledResult { public bool Cancelled { get; set; } = true; }
// getHttpPort result: the loopback HTTP endpoint's port AND its per-instance bearer token.
// Both travel ONLY over the ACL'd pipe — the token is what makes the localhost port an
// enforced boundary rather than mere obscurity (any local process/web page can reach
// loopback TCP; only holders of the token pass the host's 401 gate).
[MessagePackObject(keyAsPropertyName: true)] public sealed class HttpPortResult { public int Port { get; set; } public string Token { get; set; } = ""; }
