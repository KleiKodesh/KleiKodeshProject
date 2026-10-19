import { describe, it, expect } from 'vitest'
import { ref, watch, nextTick } from 'vue'
import {
  flushAllPersistence,
  registerPendingWriter,
  registerPositionCapture,
} from './persistenceFlush'

/**
 * The host-close flush: position captures run first, the watchers they dirty get
 * their tick to schedule writes, then every pending writer is flushed, and the
 * returned promise settles only when the writes have landed.
 */
describe('flushAllPersistence', () => {
  it('runs captures, then writers, and waits for their promises', async () => {
    const order: string[] = []
    let settled = false
    const unregisterCapture = registerPositionCapture(() => { order.push('capture') })
    const unregisterWriter = registerPendingWriter(
      () => new Promise<void>((resolve) => setTimeout(() => { settled = true; order.push('written'); resolve() }, 5)),
    )
    try {
      await flushAllPersistence()
      expect(settled).toBe(true)
      expect(order).toEqual(['capture', 'written'])
    } finally {
      unregisterCapture()
      unregisterWriter()
    }
  })

  it('lets a watcher dirtied by a capture schedule its write before writers flush', async () => {
    const state = ref(0)
    let scheduled = false
    const stop = watch(state, () => { scheduled = true })
    const unregisterCapture = registerPositionCapture(() => { state.value++ })
    let sawScheduled = false
    const unregisterWriter = registerPendingWriter(() => { sawScheduled = scheduled })
    try {
      await flushAllPersistence()
      expect(sawScheduled).toBe(true)
    } finally {
      stop()
      unregisterCapture()
      unregisterWriter()
      await nextTick()
    }
  })

  it('a throwing or rejecting saver does not stop the others or the ack', async () => {
    let otherRan = false
    const u1 = registerPositionCapture(() => { throw new Error('boom') })
    const u2 = registerPendingWriter(() => Promise.reject(new Error('put failed')))
    const u3 = registerPendingWriter(() => { otherRan = true })
    try {
      await expect(flushAllPersistence()).resolves.toBeUndefined()
      expect(otherRan).toBe(true)
    } finally {
      u1(); u2(); u3()
    }
  })

  it('an unregistered saver is not called', async () => {
    let calls = 0
    const unregister = registerPendingWriter(() => { calls++ })
    unregister()
    await flushAllPersistence()
    expect(calls).toBe(0)
  })
})
