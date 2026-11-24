import { describe, it, expect } from 'vitest'
import {
  applyCopyExclusivity,
  normalizeCopyFlags,
  type CopyExclusivityFlags,
} from './copyFlagExclusivity'

const NONE: CopyExclusivityFlags = {
  copySourcePosition: null,
  copyWithNotes: false,
  copyAsSourceWithQuotation: false,
}

describe('applyCopyExclusivity', () => {
  it('start XOR end — turning on start clears end', () => {
    const r = applyCopyExclusivity({ ...NONE, copySourcePosition: 'end' }, 'sourceStart', true)
    expect(r.copySourcePosition).toBe('start')
  })

  it('turning on end clears start', () => {
    const r = applyCopyExclusivity({ ...NONE, copySourcePosition: 'start' }, 'sourceEnd', true)
    expect(r.copySourcePosition).toBe('end')
  })

  it('notes + start-source is ALLOWED (rule 2 asymmetry is intentional)', () => {
    const r = applyCopyExclusivity({ ...NONE, copyWithNotes: true }, 'sourceStart', true)
    expect(r.copySourcePosition).toBe('start')
    expect(r.copyWithNotes).toBe(true)
  })

  it('notes ⊗ end-source — enabling end-source clears notes', () => {
    const r = applyCopyExclusivity({ ...NONE, copyWithNotes: true }, 'sourceEnd', true)
    expect(r.copySourcePosition).toBe('end')
    expect(r.copyWithNotes).toBe(false)
  })

  it('notes ⊗ end-source — enabling notes clears end-source only (not start)', () => {
    const fromEnd = applyCopyExclusivity({ ...NONE, copySourcePosition: 'end' }, 'withNotes', true)
    expect(fromEnd.copySourcePosition).toBe(null)
    expect(fromEnd.copyWithNotes).toBe(true)

    const fromStart = applyCopyExclusivity({ ...NONE, copySourcePosition: 'start' }, 'withNotes', true)
    expect(fromStart.copySourcePosition).toBe('start') // start survives
    expect(fromStart.copyWithNotes).toBe(true)
  })

  it('enabling quotation with no position defaults to start and clears notes', () => {
    const r = applyCopyExclusivity({ ...NONE, copyWithNotes: true }, 'sourceWithQuotation', true)
    expect(r.copyAsSourceWithQuotation).toBe(true)
    expect(r.copySourcePosition).toBe('start')
    expect(r.copyWithNotes).toBe(false)
  })

  it('enabling quotation KEEPS an already-set position (end stays end)', () => {
    const r = applyCopyExclusivity({ ...NONE, copySourcePosition: 'end' }, 'sourceWithQuotation', true)
    expect(r.copyAsSourceWithQuotation).toBe(true)
    expect(r.copySourcePosition).toBe('end')
  })

  it('quotation requires a position — start/end act as a radio while it is on', () => {
    const on = { copySourcePosition: 'start' as const, copyWithNotes: false, copyAsSourceWithQuotation: true }
    // switching between positions works
    expect(applyCopyExclusivity(on, 'sourceEnd', true).copySourcePosition).toBe('end')
    // unchecking the active position flips to the other, never both off
    expect(applyCopyExclusivity(on, 'sourceStart', false).copySourcePosition).toBe('end')
    const onEnd = { ...on, copySourcePosition: 'end' as const }
    expect(applyCopyExclusivity(onEnd, 'sourceEnd', false).copySourcePosition).toBe('start')
  })

  it('enabling start or end while quotation is on does NOT clear quotation', () => {
    const on = { copySourcePosition: 'start' as const, copyWithNotes: false, copyAsSourceWithQuotation: true }
    expect(applyCopyExclusivity(on, 'sourceEnd', true).copyAsSourceWithQuotation).toBe(true)
  })

  it('enabling notes clears quotation', () => {
    const on = { copySourcePosition: 'start' as const, copyWithNotes: false, copyAsSourceWithQuotation: true }
    const r = applyCopyExclusivity(on, 'withNotes', true)
    expect(r.copyWithNotes).toBe(true)
    expect(r.copyAsSourceWithQuotation).toBe(false)
    expect(r.copySourcePosition).toBe('start') // start survives notes
  })

  it('turning a position OFF clears it when quotation is off', () => {
    const start = applyCopyExclusivity({ ...NONE, copySourcePosition: 'start' }, 'sourceStart', false)
    expect(start).toEqual(NONE)
  })

  it('turning notes OFF never forces anything else on/off', () => {
    const notes = applyCopyExclusivity({ ...NONE, copyWithNotes: true }, 'withNotes', false)
    expect(notes).toEqual(NONE)
  })

  it('does not mutate the input', () => {
    const input = { ...NONE, copySourcePosition: 'end' as const }
    applyCopyExclusivity(input, 'sourceStart', true)
    expect(input.copySourcePosition).toBe('end')
  })
})

describe('normalizeCopyFlags', () => {
  it('leaves a valid combination untouched', () => {
    const valid = { ...NONE, copySourcePosition: 'start' as const, copyWithNotes: true }
    expect(normalizeCopyFlags(valid)).toEqual(valid)
  })

  it('quotation drops notes but KEEPS the position', () => {
    const bad = { copySourcePosition: 'end' as const, copyWithNotes: true, copyAsSourceWithQuotation: true }
    expect(normalizeCopyFlags(bad)).toEqual({
      copySourcePosition: 'end',
      copyWithNotes: false,
      copyAsSourceWithQuotation: true,
    })
  })

  it('quotation with no position defaults to start', () => {
    const bad = { copySourcePosition: null, copyWithNotes: false, copyAsSourceWithQuotation: true }
    expect(normalizeCopyFlags(bad)).toEqual({
      copySourcePosition: 'start',
      copyWithNotes: false,
      copyAsSourceWithQuotation: true,
    })
  })

  it('repairs the old-build combo {end-source, notes} by dropping notes', () => {
    const bad = { copySourcePosition: 'end' as const, copyWithNotes: true, copyAsSourceWithQuotation: false }
    expect(normalizeCopyFlags(bad)).toEqual({
      copySourcePosition: 'end',
      copyWithNotes: false,
      copyAsSourceWithQuotation: false,
    })
  })

  it('keeps notes + start-source (a valid combo)', () => {
    const ok = { copySourcePosition: 'start' as const, copyWithNotes: true, copyAsSourceWithQuotation: false }
    expect(normalizeCopyFlags(ok)).toEqual(ok)
  })
})
