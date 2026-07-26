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

  it('quotation ⊗ {start, end, notes} — enabling quotation clears all three', () => {
    const r = applyCopyExclusivity(
      { copySourcePosition: 'start', copyWithNotes: true, copyAsSourceWithQuotation: false },
      'sourceWithQuotation',
      true,
    )
    expect(r.copyAsSourceWithQuotation).toBe(true)
    expect(r.copySourcePosition).toBe(null)
    expect(r.copyWithNotes).toBe(false)
  })

  it('enabling start/end/notes clears quotation', () => {
    const base = { ...NONE, copyAsSourceWithQuotation: true }
    expect(applyCopyExclusivity(base, 'sourceStart', true).copyAsSourceWithQuotation).toBe(false)
    expect(applyCopyExclusivity(base, 'sourceEnd', true).copyAsSourceWithQuotation).toBe(false)
    expect(applyCopyExclusivity(base, 'withNotes', true).copyAsSourceWithQuotation).toBe(false)
  })

  it('turning a flag OFF never forces anything else on/off', () => {
    const start = applyCopyExclusivity({ ...NONE, copySourcePosition: 'start' }, 'sourceStart', false)
    expect(start).toEqual(NONE)
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

  it('repairs quotation coexisting with source + notes (quotation wins)', () => {
    const bad = { copySourcePosition: 'end' as const, copyWithNotes: true, copyAsSourceWithQuotation: true }
    expect(normalizeCopyFlags(bad)).toEqual({
      copySourcePosition: null,
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
