import { describe, expect, it } from 'vitest'
import {
  additionalLibraryErrorText,
  additionalLibraryReasonText,
  describeFailure,
  restoreServiceAfterChange,
  waitForServiceRestart,
} from './additionalLibrariesFlow'

/** A fake clock so the polling loops run instantly and deterministically. */
function fakeClock() {
  let t = 0
  return { now: () => t, sleep: async (ms: number) => { t += ms } }
}

describe('waitForServiceRestart', () => {
  it('waits for the old instance to go down, then for the new one to answer', async () => {
    const clock = fakeClock()
    // up, up, down, down, up
    const script = [true, true, false, false, true]
    let calls = 0
    const probe = async () => {
      const ok = script[Math.min(calls++, script.length - 1)]
      if (!ok) throw new Error('down')
    }
    const back = await waitForServiceRestart(probe, { ...clock })
    expect(back).toBe(true)
    expect(calls).toBe(5) // did NOT return on the early "up" answers of the old instance
  })

  it('returns true when the restart was too fast to ever observe a gap', async () => {
    const clock = fakeClock()
    const probe = async () => undefined
    expect(await waitForServiceRestart(probe, { ...clock, downWaitMs: 2000 })).toBe(true)
  })

  it('gives up (false) when the service never comes back, instead of hanging', async () => {
    const clock = fakeClock()
    const probe = async () => { throw new Error('down') }
    expect(await waitForServiceRestart(probe, { ...clock, timeoutMs: 10_000 })).toBe(false)
    expect(clock.now()).toBeGreaterThanOrEqual(10_000)
  })
})

describe('texts', () => {
  it('maps service reasons and error codes to Hebrew, never to a blank', () => {
    expect(additionalLibraryReasonText(undefined)).toBeNull()
    expect(additionalLibraryReasonText('file not found (or not visible to the service account)')).toContain('לא נמצא')
    expect(additionalLibraryReasonText('missing required table(s): book')).toContain('טבלאות')
    expect(additionalLibraryReasonText('line text unreadable: X')).toContain('טקסט')
    expect(additionalLibraryReasonText('full-text search skipped, library is damaged: X')).toContain('פגומה')
    expect(additionalLibraryReasonText('something new')).toContain('something new')
    expect(additionalLibraryErrorText('already configured')).toContain('כבר מוגדרת')
    expect(additionalLibraryErrorText('could not save the setting: x')).toContain('לשמור')
    expect(additionalLibraryErrorText('weird')).toContain('weird')
  })

  it('names the table whose numbers are out of range, in Hebrew only (no Latin word inside the RTL sentence)', () => {
    const line = additionalLibraryReasonText("ids in table 'line' exceed the per-library id range")!
    expect(line).toContain('השורות')
    expect(additionalLibraryReasonText("ids in table 'book' exceed the per-library id range")).toContain('הספרים')
    expect(additionalLibraryReasonText("ids in table 'category' exceed the per-library id range")).toContain('הקטגוריות')
    expect(line).not.toMatch(/[A-Za-z]/)
  })

  it('describes thrown failures', () => {
    const timeout = new Error('t'); timeout.name = 'ServiceCallTimeoutError'
    expect(describeFailure(timeout)).toContain('לא ענה')
    expect(describeFailure(new Error('boom'))).toContain('boom')
  })
})

describe('restoreServiceAfterChange', () => {
  it('returns as soon as a REBUILT instance answers, without starting anything', async () => {
    const clock = fakeClock()
    const answers = ['old', 'old', 'new']
    let i = 0
    let starts = 0
    const result = await restoreServiceAfterChange({
      ...clock,
      previousInstance: 'old',
      ping: async () => answers[Math.min(i++, answers.length - 1)],
      startService: async () => { starts++; return true },
    })
    expect(result).toEqual({ ok: true, startAttempts: 0 })
    expect(starts).toBe(0)
    expect(i).toBe(3)
  })

  it('starts the service itself when it stays silent, keeps probing, and succeeds when it answers', async () => {
    const clock = fakeClock()
    let running = false
    let starts = 0
    const statuses: string[] = []
    const result = await restoreServiceAfterChange({
      ...clock,
      previousInstance: 'old',
      ping: async () => { if (!running) throw new Error('down'); return 'new' },
      startService: async () => { starts++; running = true; return true },
      onStatus: (s) => statuses.push(s),
    })
    expect(result.ok).toBe(true)
    expect(starts).toBe(1)
    expect(statuses).toContain('starting')
  })

  it('retries the start when the first attempt did not bring it up, then reports honestly if none does', async () => {
    const clock = fakeClock()
    let starts = 0
    const result = await restoreServiceAfterChange({
      ...clock,
      timeoutMs: 30_000,
      ping: async () => { throw new Error('down') },
      startService: async () => { starts++; return false },
    })
    expect(result.ok).toBe(false)
    expect(starts).toBeGreaterThanOrEqual(4) // first at ~4s, then every 5s
    expect(result.startAttempts).toBe(starts)
    expect(clock.now()).toBeGreaterThanOrEqual(30_000)
  })

  it('survives a start call that throws', async () => {
    const clock = fakeClock()
    let running = false
    let calls = 0
    const result = await restoreServiceAfterChange({
      ...clock,
      previousInstance: 'old',
      ping: async () => { if (!running) throw new Error('down'); return 'new' },
      startService: async () => { calls++; if (calls === 1) throw new Error('boom'); running = true; return true },
    })
    expect(result.ok).toBe(true)
    expect(calls).toBe(2)
  })

  it('accepts the same instance after the grace period (reload too fast to observe, or an old service build)', async () => {
    const clock = fakeClock()
    const result = await restoreServiceAfterChange({
      ...clock,
      previousInstance: 'same',
      ping: async () => 'same',
      startService: async () => true,
    })
    expect(result.ok).toBe(true)
    expect(clock.now()).toBeGreaterThanOrEqual(8_000)
  })
})
