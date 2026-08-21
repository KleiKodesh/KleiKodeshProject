/**
 * Dev-only clean client for the KitveiHakodesh service.
 *
 * The app asks the service for *what it needs* by op name — it never constructs SQL, opens
 * sockets, or knows which backend answers. In dev the browser talks DIRECTLY to the service's
 * loopback HTTP host at `<base>/rpc`. That base is discovered at runtime from the dev server's
 * `/khs-endpoint` route: the service's port is PRIVATE (handed to the dev server over an ACL'd
 * named pipe, never a file), and the dev server relays only the port — not the data. In hosted
 * mode the C# host is the courier instead, so this module is never used there (callers guard on
 * `window.__webviewAction`).
 *
 * Wire format: **MessagePack** (compact binary — smaller + faster than JSON, which matters
 * most for the large FTS result sets).
 *   request  → msgpack { Op, Args }   where Args = nested msgpack bytes of the args object
 *   response ← msgpack { Ok, Result?, Error? }   where Result = nested msgpack bytes
 *
 * The service's DTOs use PascalCase keys on the wire (keyAsPropertyName); this module
 * transforms keys transparently so the rest of the app stays camelCase.
 */
import { encode as mpEncode, decode as mpDecode } from '@msgpack/msgpack'

// Cached endpoint of the service's HTTP host — base URL + the per-instance bearer token every
// data request must carry (the host 401s without it) — discovered once from /khs-endpoint and
// reused. Invalidated on failure so a service restart (new port AND new token) is picked up.
interface KhsEndpoint { base: string; token: string }
let khsEndpoint: KhsEndpoint | null = null
let khsEndpointPending: Promise<KhsEndpoint> | null = null

async function discoverEndpoint(): Promise<KhsEndpoint> {
  // The dev server answers 503 until the service has reported its endpoint over the pipe; retry.
  for (let attempt = 0; ; attempt++) {
    try {
      const res = await fetch('/khs-endpoint', { cache: 'no-store' })
      if (res.ok) {
        const { base, token } = (await res.json()) as { base: string; token: string }
        if (base && token) return { base, token }
      }
    } catch { /* dev server momentarily unreachable — retry */ }
    await new Promise((r) => setTimeout(r, Math.min(1000, 100 + attempt * 100)))
    if (attempt > 80) throw new Error('KitveiHakodesh service endpoint never became available')
  }
}

function getEndpoint(): Promise<KhsEndpoint> {
  if (khsEndpoint) return Promise.resolve(khsEndpoint)
  return (khsEndpointPending ??= discoverEndpoint().then((e) => {
    khsEndpoint = e
    khsEndpointPending = null
    return e
  }))
}

/**
 * POST a request body to the service, riding out a brief outage. The service can be momentarily
 * down — its own setSeforimDbPath self-restart, or the dev supervisor respawning it (which also
 * changes the ephemeral port AND the bearer token) — during which a direct fetch to the loopback
 * host rejects with a TypeError. A 401 means our token is stale (new service instance). Either
 * way we invalidate the cached endpoint, re-discover, and retry. A retried fetch replays the
 * same ArrayBuffer body safely (it is not a consumed stream).
 */
async function postRpc(path: string, body: BodyInit, signal?: AbortSignal): Promise<Response> {
  let lastErr: unknown
  for (let attempt = 0; attempt < 6; attempt++) {
    // Checked at the top of every attempt, not only around the fetch: the caller can abort
    // while we are in a backoff delay or inside endpoint discovery's own retry loop.
    if (signal?.aborted) throw signal.reason ?? new Error('aborted')
    try {
      const { base, token } = await getEndpoint()
      if (signal?.aborted) throw signal.reason ?? new Error('aborted')
      const res = await fetch(`${base}${path}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/octet-stream', 'X-KHS-Token': token },
        body,
        signal,
      })
      if (res.status === 401) {
        // Stale token (service restarted onto the same port) — re-discover and retry.
        khsEndpoint = null
        lastErr = new Error('service rejected the token (401)')
        await new Promise((r) => setTimeout(r, 60 + attempt * 80))
        continue
      }
      return res
    } catch (err) {
      if (signal?.aborted) throw err // a real cancel, not a transient outage — don't retry
      lastErr = err
      khsEndpoint = null // force re-discovery — port/token may have changed on a respawn
      await new Promise((r) => setTimeout(r, 60 + attempt * 80))
    }
  }
  throw lastErr
}

const lowerFirst = (k: string) => (k ? k.charAt(0).toLowerCase() + k.slice(1) : k)
const upperFirst = (k: string) => (k ? k.charAt(0).toUpperCase() + k.slice(1) : k)

// A few wire fields aren't plain PascalCase↔camelCase. SenseRow.SourceId is snake_case
// (source_id) in the hosted JSON path via [JsonPropertyName], so the dev path must deliver
// the same name to keep dictionary consumers identical across hosted/dev.
const decodeKey = (k: string) => (k === 'SourceId' ? 'source_id' : lowerFirst(k))

/** Recursively transform object keys (arrays + nested objects); leaves values, including
 *  binary (Uint8Array/ArrayBuffer), untouched. */
function transformKeys(v: any, fn: (k: string) => string): any {
  if (Array.isArray(v)) return v.map((x) => transformKeys(x, fn))
  if (v && typeof v === 'object' && !(v instanceof Uint8Array) && !(v instanceof ArrayBuffer)) {
    const out: Record<string, unknown> = {}
    for (const k in v) out[fn(k)] = transformKeys((v as Record<string, unknown>)[k], fn)
    return out
  }
  return v
}

function encodeRequest(op: string, args: object): BodyInit {
  const argsBytes = mpEncode(transformKeys(args ?? {}, upperFirst))
  // Copy to an exact-size ArrayBuffer: msgpack's Uint8Array may be a view into a larger
  // pooled buffer, and it types cleanly as a BodyInit for fetch.
  return mpEncode({ Op: op, Args: argsBytes }).slice().buffer
}

interface Envelope {
  Ok: boolean
  Result?: Uint8Array
  Error?: string
}

/**
 * Call a service op and return its result, throwing on a service-side error.
 *
 * `signal` is optional but worth passing from any view that can be torn down while a call is
 * in flight: without it there is no way out of the retry ladder, and a service that is
 * genuinely down leaves the call pending for minutes (six attempts, each re-entering
 * endpoint discovery's own retry loop).
 */
export async function serviceCall<T = unknown>(
  op: string,
  args: object = {},
  signal?: AbortSignal,
): Promise<T> {
  const res = await postRpc('/rpc', encodeRequest(op, args), signal)
  if (!res.ok) throw new Error(`service '${op}' failed: ${res.status} ${res.statusText}`)
  const env = mpDecode(new Uint8Array(await res.arrayBuffer())) as Envelope
  if (!env.Ok) throw new Error(env.Error || `service '${op}' error`)
  if (!env.Result || env.Result.length === 0) return undefined as T
  return transformKeys(mpDecode(env.Result), decodeKey) as T
}

/** Fire-and-forget variant (warmups etc.) — swallows every error. */
export function serviceCallVoid(op: string, args: object = {}): void {
  postRpc('/rpc', encodeRequest(op, args)).catch(() => {})
}

/**
 * Call a STREAMING service op: the service pushes many result frames over one
 * connection (via the /khs-stream courier) and this generator yields each decoded
 * frame as it arrives — no polling anywhere. Frames on the wire keep the pipe's
 * 4-byte LE length prefix; each frame body is a normal {Ok, Result, Error} envelope.
 * Aborting `signal` (or breaking out of the loop) closes the connection, which is
 * the service's cancel signal.
 */
export async function* serviceStream<T = unknown>(
  op: string,
  args: object = {},
  signal?: AbortSignal,
): AsyncGenerator<T, void, void> {
  const res = await postRpc('/rpc-stream', encodeRequest(op, args), signal)
  if (!res.ok || !res.body) throw new Error(`service '${op}' stream failed: ${res.status} ${res.statusText}`)

  const reader = res.body.getReader()
  let buf = new Uint8Array(0)
  try {
    for (;;) {
      const { value, done } = await reader.read()
      if (done) break
      if (!value || value.length === 0) continue

      const merged = new Uint8Array(buf.length + value.length)
      merged.set(buf); merged.set(value, buf.length)
      buf = merged

      // Drain every complete frame in the buffer.
      for (;;) {
        if (buf.length < 4) break
        const len = new DataView(buf.buffer, buf.byteOffset, 4).getInt32(0, true)
        if (buf.length < 4 + len) break
        const frame = buf.subarray(4, 4 + len)
        buf = buf.subarray(4 + len)

        const env = mpDecode(frame) as Envelope
        if (!env.Ok) throw new Error(env.Error || `service '${op}' stream error`)
        if (env.Result && env.Result.length > 0)
          yield transformKeys(mpDecode(env.Result), decodeKey) as T
      }
    }
  } finally {
    reader.cancel().catch(() => {})
  }
}
