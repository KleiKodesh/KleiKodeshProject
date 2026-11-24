// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest'
import { effectScope } from 'vue'
import { useCoalescedWrite } from './useCoalescedWrite'
import { flushAllPersistence } from '@/webview-host/persistenceFlush'

/**
 * A scheduled write that the host-close flush reaches before its timer fires, and
 * that is not written twice afterwards. The same registration is dropped with the
 * scope that created it.
 */
describe('useCoalescedWrite host-close flush', () => {
  it('flushes a pending write once and hands its promise to the flush', async () => {
    vi.useFakeTimers()
    const scope = effectScope()
    let writes = 0
    let landed = false
    scope.run(() => {
      const write = useCoalescedWrite(() => {
        writes++
        return new Promise<void>((resolve) => { landed = true; resolve() })
      })
      write.schedule()
    })
    try {
      const flushed = flushAllPersistence()
      await vi.advanceTimersByTimeAsync(0)
      await flushed
      expect(writes).toBe(1)
      expect(landed).toBe(true)
      await vi.advanceTimersByTimeAsync(1000)
      expect(writes).toBe(1)
    } finally {
      scope.stop()
      vi.useRealTimers()
    }
  })

  it('does nothing for a writer with no pending write, and drops it with its scope', async () => {
    const scope = effectScope()
    let writes = 0
    scope.run(() => { useCoalescedWrite(() => { writes++ }) })
    await flushAllPersistence()
    expect(writes).toBe(0)

    scope.run(() => undefined)
    scope.stop()
    await flushAllPersistence()
    expect(writes).toBe(0)
  })
})
