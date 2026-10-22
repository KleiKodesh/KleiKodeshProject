import { describe, expect, it } from 'vitest'
import {
  additionalLibraryErrorText,
  additionalLibraryReasonText,
  describeFailure,
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

  it('describes thrown failures', () => {
    const timeout = new Error('t'); timeout.name = 'ServiceCallTimeoutError'
    expect(describeFailure(timeout)).toContain('לא ענה')
    expect(describeFailure(new Error('boom'))).toContain('boom')
  })
})
