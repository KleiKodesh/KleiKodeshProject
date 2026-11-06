import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { encode } from '@msgpack/msgpack'

// serviceCall reads window.__webviewAction to pick the hosted relay (no endpoint discovery).
beforeEach(() => {
  ;(globalThis as any).window = { __webviewAction: () => {}, addEventListener() {}, removeEventListener() {} }
})
afterEach(() => {
  vi.unstubAllGlobals()
  vi.useRealTimers()
  delete (globalThis as any).window
})

describe('serviceCall timeout', () => {
  it('rejects with ServiceCallTimeoutError when the service never answers', async () => {
    // A fetch that hangs until aborted — the "service accepted the connection and went silent" case.
    vi.stubGlobal('fetch', (_url: string, init: RequestInit) =>
      new Promise((_resolve, reject) => {
        init.signal?.addEventListener('abort', () => reject(init.signal!.reason))
      }))
    const { serviceCall, ServiceCallTimeoutError } = await import('./serviceClient')
    const started = Date.now()
    await expect(serviceCall('getAdditionalSeforimLibraries', {}, undefined, 150)).rejects.toBeInstanceOf(
      ServiceCallTimeoutError,
    )
    expect(Date.now() - started).toBeLessThan(2000)
  })

  it('does not interfere with a call that answers in time', async () => {
    const result = encode({ Libraries: [] })
    const body = encode({ Ok: true, Result: result })
    vi.stubGlobal('fetch', async () => new Response(body, { status: 200 }))
    const { serviceCall } = await import('./serviceClient')
    const r = await serviceCall<{ libraries: unknown[] }>('x', {}, undefined, 5000)
    expect(r.libraries).toEqual([])
  })

  it('still honours the caller\'s own abort signal', async () => {
    vi.stubGlobal('fetch', (_url: string, init: RequestInit) =>
      new Promise((_r, reject) => init.signal?.addEventListener('abort', () => reject(init.signal!.reason))))
    const { serviceCall } = await import('./serviceClient')
    const ac = new AbortController()
    const p = serviceCall('x', {}, ac.signal, 60_000)
    setTimeout(() => ac.abort(new Error('caller aborted')), 50)
    await expect(p).rejects.toThrow('caller aborted')
  })
})
