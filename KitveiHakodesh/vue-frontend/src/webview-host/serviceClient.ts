/**
 * The ONE client for the KitveiHakodesh service, dev and hosted alike.
 *
 * The app asks the service for *what it needs* by op name — it never constructs SQL, opens
 * sockets, or knows which backend answers. Only the base URL differs per runtime:
 *
 *   dev     — the browser talks DIRECTLY to the service's loopback HTTP host at `<base>/rpc`.
 *             That base is discovered at runtime from the dev server's `/khs-endpoint` route:
 *             the service's port is PRIVATE (handed to the dev server over an ACL'd named pipe,
 *             never a file), and the dev server relays only the port — not the data.
 *   hosted  — the same `/rpc` and `/rpc-stream` POSTs go to `http://kitvei-service`, a virtual
 *             host the C# host (KitveiHakodeshLib ServiceRpcRelay) answers by relaying the
 *             bytes to the service's named pipe. No token: the pipe's ACL is the boundary.
 *
 * Wire format: **MessagePack** (compact binary — smaller + faster than JSON, which matters
 * most for the large FTS result sets).
 *   request  → msgpack { Op, Args }   where Args = nested msgpack bytes of the args object
 *   response ← msgpack { Ok, Result?, Error? }   where Result = nested msgpack bytes
 *
 * The service's DTOs use PascalCase keys on the wire (keyAsPropertyName); this module
 * transforms keys transparently so the rest of the app stays camelCase.
 */
import { encode as mpEncode, decode as mpDecode, Decoder } from '@msgpack/msgpack'
import { onWebviewEvent } from './seforimDb'
import { serviceCallEnded, serviceCallStarted } from './serviceIdle'
import {
  isPersistentReason,
  markServiceAvailable,
  markServiceUnavailable,
  parseUnavailableReason,
  ServiceUnavailableError,
  type ServiceUnavailableReason,
} from './serviceAvailability'

// Cached endpoint of the service's HTTP host — base URL + the per-instance bearer token every
// data request must carry (the host 401s without it) — discovered once from /khs-endpoint and
// reused. Invalidated on failure so a service restart (new port AND new token) is picked up.
interface KhsEndpoint { base: string; token: string }
let khsEndpoint: KhsEndpoint | null = null
let khsEndpointPending: Promise<KhsEndpoint> | null = null

async function discoverEndpoint(): Promise<KhsEndpoint> {
  // The dev server answers 503 until the service has reported its endpoint over the pipe; retry.
  const t0 = performance.now()
  for (let attempt = 0; ; attempt++) {
    try {
      const res = await fetch('/khs-endpoint', { cache: 'no-store' })
      if (res.ok) {
        const { base, token } = (await res.json()) as { base: string; token: string }
        if (base && token) {
          console.info(`[service] endpoint ${base} found after ${attempt + 1} tries, ${Math.round(performance.now() - t0)} ms`)
          return { base, token }
        }
      }
      if (attempt % 10 === 0) console.warn(`[service] /khs-endpoint try ${attempt + 1}: ${res.status} (service not reported yet)`)
    } catch (e) {
      if (attempt % 10 === 0) console.warn(`[service] /khs-endpoint try ${attempt + 1}: dev server unreachable`, e)
    }
    await new Promise((r) => setTimeout(r, Math.min(1000, 100 + attempt * 100)))
    if (attempt > 80) {
      console.error(`[service] endpoint never became available (${Math.round(performance.now() - t0)} ms)`)
      throw new Error('KitveiHakodesh service endpoint never became available')
    }
  }
}

/** The virtual host KitveiHakodeshLib relays to the service pipe (ServiceRpcRelay.BaseUrl). */
const HOSTED_RELAY_ENDPOINT: KhsEndpoint = { base: 'http://kitvei-service', token: '' }

function getEndpoint(): Promise<KhsEndpoint> {
  if (typeof window.__webviewAction === 'function') return Promise.resolve(HOSTED_RELAY_ENDPOINT)
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
  let lastReason: ServiceUnavailableReason = 'NotRunning'
  let lastDetail = ''
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
      if (res.status === 401 || res.status === 503) {
        // 401: stale token (service restarted onto the same port) — re-discover and retry.
        // 503: the hosted relay could not reach the service. X-KHS-Unavailable says why; only
        // NotRunning (it is restarting) is worth retrying - the others need the user to act.
        khsEndpoint = null
        lastReason = res.status === 503 ? parseUnavailableReason(res.headers.get('X-KHS-Unavailable')) : 'NotRunning'
        lastDetail = res.status === 401 ? 'service rejected the token (401)' : await res.text()
        console.warn(`[service] ${path} attempt ${attempt + 1}: ${res.status} ${lastReason} ${lastDetail}`)
        if (isPersistentReason(lastReason)) break
        await new Promise((r) => setTimeout(r, 60 + attempt * 80))
        continue
      }
      markServiceAvailable()
      return res
    } catch (err) {
      if (signal?.aborted) throw err // a real cancel, not a transient outage — don't retry
      lastReason = 'NotRunning'
      lastDetail = err instanceof Error ? err.message : String(err)
      console.warn(`[service] ${path} attempt ${attempt + 1}: fetch failed (${lastDetail})`)
      khsEndpoint = null // force re-discovery — port/token may have changed on a respawn
      await new Promise((r) => setTimeout(r, 60 + attempt * 80))
    }
  }
  // Out of attempts: one banner for the whole app says what is wrong (ServiceUnavailableBanner).
  console.error(`[service] ${path} gave up after 6 attempts: ${lastReason} ${lastDetail}`)
  markServiceUnavailable(lastReason)
  throw new ServiceUnavailableError(lastReason, lastDetail)
}

const lowerFirst = (k: string) => (k ? k.charAt(0).toLowerCase() + k.slice(1) : k)
const upperFirst = (k: string) => (k ? k.charAt(0).toUpperCase() + k.slice(1) : k)

// A few wire fields aren't plain PascalCase<->camelCase. SenseRow.SourceId reaches the
// app as source_id - the name the dictionary consumers were written against.
const decodeKey = (k: string) => (k === 'SourceId' ? 'source_id' : lowerFirst(k))

/** Wire key -> app key, memoised: the same few dozen DTO keys recur across every row of
 *  every reply, so each key string is converted once per process rather than per object. */
const decodedKeyByWireKey = new Map<string, string>()
function decodeMapKey(key: unknown): string | number {
  if (typeof key === 'number') return key
  if (typeof key !== 'string') throw new Error(`service reply map key must be string or number, got ${typeof key}`)
  let out = decodedKeyByWireKey.get(key)
  if (out === undefined) {
    out = decodeKey(key)
    decodedKeyByWireKey.set(key, out)
  }
  return out
}

/** Decodes a reply payload with its keys already camelCased: the rename happens while
 *  msgpack builds each object, so there is no second pass and no copy of the decoded tree.
 *  The Decoder is safe to share - it clones itself when re-entered. Envelopes are decoded
 *  with the plain `mpDecode` because their keys (Ok/Result/Error) are read in PascalCase. */
const replyDecoder = new Decoder({ mapKeyConverter: decodeMapKey })
const decodeReply = (bytes: Uint8Array): unknown => replyDecoder.decode(bytes)

/** Recursively PascalCase the keys of an outgoing args object (arrays + nested objects);
 *  leaves values, including binary (Uint8Array/ArrayBuffer), untouched. An array whose
 *  first element is a primitive is returned as-is: wire arrays are homogeneous. */
function transformKeys(v: any, fn: (k: string) => string): any {
  if (Array.isArray(v)) {
    if (v.length > 0 && (typeof v[0] !== 'object' || v[0] === null)) return v
    return v.map((x) => transformKeys(x, fn))
  }
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
  /** Full C# exception (ToString) for the console; Error is the short message. */
  Detail?: string
}

/** A failed envelope: print the service's full failure to the console, then throw the short message. */
function throwEnvelopeError(op: string, env: Envelope, fallback: string): never {
  console.error(`[service] '${op}' failed:\n${env.Detail || env.Error || fallback}`)
  throw new Error(env.Error || fallback)
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
  serviceCallStarted()
  try {
    return await serviceCallCore<T>(op, args, signal)
  } finally {
    serviceCallEnded()
  }
}

async function serviceCallCore<T>(op: string, args: object, signal?: AbortSignal): Promise<T> {
  const t0 = performance.now()
  console.debug(`[service] -> ${op}`, args)
  const res = await postRpc('/rpc', encodeRequest(op, args), signal)
  if (!res.ok) {
    const msg = `service '${op}' failed: ${res.status} ${res.statusText}`
    console.error(`[service] ${msg}\n${await res.text().catch(() => '')}`)
    throw new Error(msg)
  }
  const env = mpDecode(new Uint8Array(await res.arrayBuffer())) as Envelope
  if (!env.Ok) throwEnvelopeError(op, env, `service '${op}' error`)
  // An op that caught its own failure still answers Ok; Detail carries the exception.
  if (env.Detail) console.error(`[service] '${op}' failed:\n${env.Detail}`)
  const ms = Math.round(performance.now() - t0)
  if (!env.Result || env.Result.length === 0) {
    console.debug(`[service] <- ${op} ${ms} ms, EMPTY result`)
    return undefined as T
  }
  console.debug(`[service] <- ${op} ${ms} ms, ${env.Result.length} bytes`)
  return decodeReply(env.Result) as T
}

/** Fire-and-forget variant (warmups etc.) — swallows every error. */
export function serviceCallVoid(op: string, args: object = {}): void {
  postRpc('/rpc', encodeRequest(op, args)).catch(() => {})
}

/**
 * The hosted leg of serviceStream. WebView2 cannot deliver a response body progressively, so
 * the C# relay (ServiceRpcRelay) does not answer `/rpc-stream` with the frames: it pushes them
 * as SHARED BUFFERS (`sharedbufferreceived`) holding the raw MessagePack frames — every frame
 * that arrived since the previous push, laid out [u32 count] then per frame [u32 len][bytes],
 * little-endian — tagged `{ streamId, ended, error }`. A plain `serviceStreamEnd` message is
 * only the relay's fallback when a buffer could not be posted. The stream id is chosen HERE
 * and sent with the POST,
 * so the listener below is registered before the first frame can possibly arrive. Aborting
 * posts `/rpc-stream-cancel`, which closes the pipe — the service's cancel signal.
 */
/** WebView2's `sharedbufferreceived` event: the buffer must be released once read. */
interface SharedBufferReceivedEvent {
  additionalData: unknown
  getBuffer(): ArrayBuffer
}
interface HostedWebview {
  addEventListener(type: 'sharedbufferreceived', fn: (e: SharedBufferReceivedEvent) => void): void
  removeEventListener(type: 'sharedbufferreceived', fn: (e: SharedBufferReceivedEvent) => void): void
  releaseBuffer(buffer: ArrayBuffer): void
}

/** Frames consumed between `/rpc-stream-ack` credits; below the relay's StreamFrameWindow. */
const STREAM_ACK_EVERY = 8

async function* hostedRelayStream<T>(
  op: string,
  args: object,
  signal?: AbortSignal,
): AsyncGenerator<T, void, void> {
  const streamId = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
  const queue: Uint8Array[] = []
  let ended = false
  let endError: string | null = null
  let wake: (() => void) | null = null
  const notify = () => { wake?.(); wake = null }

  const endStream = (error: unknown) => {
    ended = true
    endError = error ? String(error) : null
    notify()
  }
  const webview = (window as unknown as { chrome?: { webview?: HostedWebview } }).chrome?.webview
  const onSharedBuffer = (e: SharedBufferReceivedEvent) => {
    const tag = e.additionalData as { streamId?: string; ended?: boolean; error?: string } | null
    if (tag?.streamId !== streamId) return
    const shared = e.getBuffer()
    try {
      // Copied out, because the shared memory is released below and frames are decoded later.
      const bytes = new Uint8Array(shared.slice(0))
      const view = new DataView(bytes.buffer)
      const count = view.getUint32(0, true)
      let at = 4
      for (let i = 0; i < count; i++) {
        const len = view.getUint32(at, true)
        queue.push(bytes.subarray(at + 4, at + 4 + len))
        at += 4 + len
      }
    } finally {
      webview?.releaseBuffer(shared)
    }
    if (tag.ended) endStream(tag.error)
    else notify()
  }
  webview?.addEventListener('sharedbufferreceived', onSharedBuffer)
  const unregisterFallbackEnd = onWebviewEvent((msg) => {
    if (msg.streamId === streamId && msg.event === 'serviceStreamEnd') endStream(msg.error)
  })
  const unregister = () => {
    webview?.removeEventListener('sharedbufferreceived', onSharedBuffer)
    unregisterFallbackEnd()
  }
  // The relay pushes only while it holds credits, which the page returns as it CONSUMES
  // frames (after each yield resumes). That carries the pipe's backpressure through the
  // fire-and-forget push, so a slow consumer holds back the service instead of piling
  // every frame of a huge result set up in the queue (renderer out of memory).
  let unacked = 0
  const ack = () => {
    const n = unacked
    unacked = 0
    fetch(`${HOSTED_RELAY_ENDPOINT.base}/rpc-stream-ack?id=${encodeURIComponent(streamId)}&n=${n}`, { method: 'POST' })
      .catch(() => {})
  }
  const onAbort = () => { ended = true; notify() }
  signal?.addEventListener('abort', onAbort, { once: true })

  try {
    const res = await postRpc(`/rpc-stream?id=${encodeURIComponent(streamId)}`, encodeRequest(op, args), signal)
    if (!res.ok) throw new Error(`service '${op}' stream failed: ${res.status} ${res.statusText}`)
    for (;;) {
      while (queue.length) {
        const frame = queue.shift()!
        const env = mpDecode(frame) as Envelope
        if (!env.Ok) throwEnvelopeError(op, env, `service '${op}' stream error`)
        if (env.Result && env.Result.length > 0)
          yield decodeReply(env.Result) as T
        if (++unacked >= STREAM_ACK_EVERY) ack()
      }
      if (unacked) ack() // queue drained: hand back the rest so the relay never stalls
      if (signal?.aborted) return
      if (ended) {
        if (endError) throw new Error(endError)
        return
      }
      await new Promise<void>((resolve) => { wake = resolve })
    }
  } finally {
    unregister()
    signal?.removeEventListener('abort', onAbort)
    // Closing early (abort, or the consumer broke out of the loop) must close the pipe, or the
    // service keeps streaming into nobody. A stream that ended on its own is already closed.
    if (!ended || signal?.aborted)
      fetch(`${HOSTED_RELAY_ENDPOINT.base}/rpc-stream-cancel?id=${encodeURIComponent(streamId)}`, { method: 'POST' })
        .catch(() => {})
  }
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
  if (typeof window.__webviewAction === 'function') {
    yield* hostedRelayStream<T>(op, args, signal)
    return
  }
  console.debug(`[service] -> stream ${op}`, args)
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
        if (!env.Ok) throwEnvelopeError(op, env, `service '${op}' stream error`)
        if (env.Result && env.Result.length > 0)
          yield decodeReply(env.Result) as T
      }
    }
  } finally {
    reader.cancel().catch(() => {})
  }
}
