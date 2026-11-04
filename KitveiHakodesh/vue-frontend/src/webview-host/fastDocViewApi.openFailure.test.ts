// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { parseFastDocViewOpenFailure } from './fastDocViewApi'

describe('parseFastDocViewOpenFailure', () => {
  it('reads the code and the detail of the service marker', () => {
    expect(parseFastDocViewOpenFailure(new Error('service error: FastDocViewOpenFailed:encrypted: Encrypted (password-protected)')))
      .toEqual({ code: 'encrypted', detail: 'Encrypted (password-protected)' })
  })
  it('ignores unknown codes and plain errors', () => {
    expect(parseFastDocViewOpenFailure(new Error('FastDocViewOpenFailed:banana: x'))).toBeNull()
    expect(parseFastDocViewOpenFailure(new Error('boom'))).toBeNull()
    expect(parseFastDocViewOpenFailure(null)).toBeNull()
  })
})
