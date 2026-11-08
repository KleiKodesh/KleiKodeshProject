import { describe, expect, it } from 'vitest'
import {
  NOTE_ID_RESERVED_START, decodeNoteLine, encodeNoteLine, isNoteId, noteIndexOf, sourceBookOf, sourceLineOf,
} from './noteIds'

describe('noteIds', () => {
  const R = NOTE_ID_RESERVED_START
  it('splits real and virtual at 2^52', () => {
    expect(R).toBe(4503599627370496)
    expect(isNoteId(R - 1)).toBe(false)
    expect(isNoteId(R)).toBe(true)
  })
  it('the largest id is Number.MAX_SAFE_INTEGER', () => {
    expect(encodeNoteLine(2 ** 36 - 1, 65535)).toBe(Number.MAX_SAFE_INTEGER)
    expect(decodeNoteLine(Number.MAX_SAFE_INTEGER)).toEqual({ sourceLineId: 2 ** 36 - 1, noteIndex: 65535 })
  })
  it('round-trips', () => {
    const id = encodeNoteLine(10350062, 54)
    expect(decodeNoteLine(id)).toEqual({ sourceLineId: 10350062, noteIndex: 54 })
    expect(noteIndexOf(id)).toBe(54)
    expect(noteIndexOf(11)).toBeNull()
  })
  it('maps to the source', () => {
    expect(sourceLineOf(encodeNoteLine(11, 1))).toBe(11)
    expect(sourceLineOf(11)).toBe(11)
    expect(sourceBookOf(R + 8440)).toBe(8440)
    expect(sourceBookOf(5)).toBe(5)
  })
})
