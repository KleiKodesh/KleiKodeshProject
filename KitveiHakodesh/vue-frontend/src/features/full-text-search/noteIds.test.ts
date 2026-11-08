import { describe, expect, it } from 'vitest'
import { NOTE_ID_RESERVED_START, decodeNoteLine, isNoteId, sourceBookOf, sourceLineOf } from './noteIds'

describe('noteIds', () => {
  const R = NOTE_ID_RESERVED_START
  it('splits real and virtual at 2^30', () => {
    expect(R).toBe(1073741824)
    expect(isNoteId(R - 1)).toBe(false)
    expect(isNoteId(R)).toBe(true)
  })
  it('decodes the largest id', () => {
    expect(decodeNoteLine(2 ** 31 - 1)).toEqual({ sourceLineId: 2 ** 24 - 1, noteIndex: 63 })
  })
  it('maps to the source', () => {
    expect(sourceLineOf(R + (11 << 6) + 1)).toBe(11)
    expect(sourceLineOf(11)).toBe(11)
    expect(sourceBookOf(R + 8440)).toBe(8440)
    expect(sourceBookOf(5)).toBe(5)
  })
})
