/**
 * Dev-only clean client for the KitveiHakodesh service.
 *
 * The app asks the service for *what it needs* by op name — it never constructs
 * SQL, opens pipes, or knows which backend answers. In dev the courier is the
 * Vite middleware at `/khs`, which forwards the request frame over the
 * `KitveiHakodesh` named pipe. In hosted mode the C# host is the courier instead,
 * so this module is never used there (callers guard on `window.__webviewAction`).
 *
 * Wire format: **MessagePack** (compact binary — smaller + faster than JSON,
 * which matters most for the large FTS result sets).
 *   request  → msgpack { Op, Args }   where Args = nested msgpack bytes of the args object
 *   response ← msgpack { Ok, Result?, Error? }   where Result = nested msgpack bytes
 *
 * The service's DTOs use PascalCase keys on the wire (keyAsPropertyName); this module
 * transforms keys transparently so the rest of the app stays camelCase.
 */
import { encode as mpEncode, decode as mpDecode } from '@msgpack/msgpack'

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

/** Call a service op and return its result, throwing on a service-side error. */
export async function serviceCall<T = unknown>(op: string, args: object = {}): Promise<T> {
  const res = await fetch('/khs', {
    method: 'POST',
    headers: { 'Content-Type': 'application/octet-stream' },
    body: encodeRequest(op, args),
  })
  if (!res.ok) throw new Error(`service '${op}' failed: ${res.status} ${res.statusText}`)
  const env = mpDecode(new Uint8Array(await res.arrayBuffer())) as Envelope
  if (!env.Ok) throw new Error(env.Error || `service '${op}' error`)
  if (!env.Result || env.Result.length === 0) return undefined as T
  return transformKeys(mpDecode(env.Result), decodeKey) as T
}

/** Fire-and-forget variant (warmups etc.) — swallows every error. */
export function serviceCallVoid(op: string, args: object = {}): void {
  fetch('/khs', {
    method: 'POST',
    headers: { 'Content-Type': 'application/octet-stream' },
    body: encodeRequest(op, args),
  }).catch(() => {})
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
  const res = await fetch('/khs-stream', {
    method: 'POST',
    headers: { 'Content-Type': 'application/octet-stream' },
    body: encodeRequest(op, args),
    signal,
  })
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
