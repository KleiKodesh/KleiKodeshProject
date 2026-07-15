/**
 * Dev-only clean client for the KitveiHakodesh service.
 *
 * The app asks the service for *what it needs* by op name — it never constructs
 * SQL, opens pipes, or knows which backend answers. In dev the courier is the
 * Vite middleware at `/khs`, which forwards the `{op,args}` envelope over the
 * `KitveiHakodesh` named pipe. In hosted mode the C# host is the courier instead,
 * so this module is never used there (callers guard on `window.__webviewAction`).
 *
 * Wire envelope:
 *   request  → { op, args }
 *   response ← { ok: true, result } | { ok: false, error }
 */

interface RpcEnvelope<T> {
  ok: boolean
  result?: T
  error?: string
}

/** Call a service op and return its result, throwing on a service-side error. */
export async function serviceCall<T = unknown>(op: string, args: object = {}): Promise<T> {
  const res = await fetch('/khs', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ op, args }),
  })
  if (!res.ok) throw new Error(`service '${op}' failed: ${res.status} ${res.statusText}`)
  const env = (await res.json()) as RpcEnvelope<T>
  if (!env.ok) throw new Error(env.error || `service '${op}' error`)
  return env.result as T
}

/** Fire-and-forget variant (warmups etc.) — swallows every error. */
export function serviceCallVoid(op: string, args: object = {}): void {
  fetch('/khs', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ op, args }),
  }).catch(() => {})
}
