import { describe, expect, it } from 'vitest'
import { parseBookLink } from './appDeepLink'

describe('parseBookLink', () => {
  it('reads the three link families like HostLink.TryParse', () => {
    expect(parseBookLink('otzaria://open/book/12?index=3')).toEqual({ scheme: 'otzaria', bookId: 12, index: 3 })
    expect(parseBookLink('kitveihakodeshapp://book/7/?index=0&mark')).toEqual({ scheme: 'kitveihakodeshapp', bookId: 7, index: 0 })
    expect(parseBookLink('seforimapp://book/7?index=5')).toEqual({ scheme: 'kitveihakodeshapp', bookId: 7, index: 5 })
    expect(parseBookLink('zayit://book/4/line/99')).toEqual({ scheme: 'zayit', bookId: 4, lineId: 99 })
  })
  it('rejects links without a line and other URLs', () => {
    expect(parseBookLink('otzaria://open/book/12')).toBeNull()
    expect(parseBookLink('otzaria://open/book/12?index=-1')).toBeNull()
    expect(parseBookLink('https://example.com/book/12?index=3')).toBeNull()
  })
})
