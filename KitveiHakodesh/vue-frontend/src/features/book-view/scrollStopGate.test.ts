import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createScrollStopGate } from './scrollStopGate'

describe('scrollStopGate', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('a run of scroll steps fires once, on scrollend', () => {
    const onStop = vi.fn()
    const gate = createScrollStopGate(onStop)
    for (let i = 0; i < 30; i++) { gate.moved(); vi.advanceTimersByTime(50) }
    expect(onStop).not.toHaveBeenCalled()
    gate.ended()
    gate.ended()
    expect(onStop).toHaveBeenCalledTimes(1)
    vi.advanceTimersByTime(1000)
    expect(onStop).toHaveBeenCalledTimes(1)
  })

  it('falls back to the quiet period when scrollend never comes', () => {
    const onStop = vi.fn()
    const gate = createScrollStopGate(onStop, 300)
    gate.moved()
    vi.advanceTimersByTime(299)
    gate.moved()
    vi.advanceTimersByTime(299)
    expect(onStop).not.toHaveBeenCalled()
    vi.advanceTimersByTime(1)
    expect(onStop).toHaveBeenCalledTimes(1)
  })

  it('scrollend with no movement owed fires nothing', () => {
    const onStop = vi.fn()
    createScrollStopGate(onStop).ended()
    expect(onStop).not.toHaveBeenCalled()
  })

  it('a scrollbar drag fires once, on release, ignoring scrollend and pauses meanwhile', () => {
    const onStop = vi.fn()
    const gate = createScrollStopGate(onStop, 300)
    gate.pointerDown()
    gate.moved(); gate.ended()
    vi.advanceTimersByTime(2000)
    gate.moved(); gate.ended()
    expect(onStop).not.toHaveBeenCalled()
    gate.pointerUp()
    expect(onStop).toHaveBeenCalledTimes(1)
  })

  it('a click on the bar that moved nothing fires nothing', () => {
    const onStop = vi.fn()
    const gate = createScrollStopGate(onStop)
    gate.pointerDown(); gate.pointerUp()
    expect(onStop).not.toHaveBeenCalled()
  })

  it('cancel drops the owed stop', () => {
    const onStop = vi.fn()
    const gate = createScrollStopGate(onStop, 300)
    gate.moved(); gate.cancel()
    vi.advanceTimersByTime(1000); gate.ended()
    expect(onStop).not.toHaveBeenCalled()
  })
})
