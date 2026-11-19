import { describe, expect, it } from 'vitest'
import { isLibraryAppLink, parseBookLink, parseSearchLink } from './appDeepLink'

const TITLE = '\u05d1\u05e8\u05d0\u05e9\u05d9\u05ea'
const HEADING = '\u05e4\u05e8\u05e7 \u05d0'
const ENCODED = encodeURIComponent(TITLE)

describe('parseBookLink', () => {
  it('reads the links with a line, like HostLink.TryParse', () => {
    expect(parseBookLink('otzaria://open/book/12?index=3')).toEqual({ scheme: 'otzaria', bookId: 12, index: 3 })
    expect(parseBookLink('kitveihakodeshapp://book/7/?index=0&mark')).toEqual({ scheme: 'kitveihakodeshapp', bookId: 7, index: 0 })
    expect(parseBookLink('seforimapp://book/7?index=5')).toEqual({ scheme: 'kitveihakodeshapp', bookId: 7, index: 5 })
    expect(parseBookLink('zayit://book/4/line/99')).toEqual({ scheme: 'zayit', bookId: 4, lineId: 99 })
  })
  it('opens at the top without an index (or with a bad one)', () => {
    expect(parseBookLink('otzaria://open/book/12')).toEqual({ scheme: 'otzaria', bookId: 12 })
    expect(parseBookLink('otzaria://open/book/12/')).toEqual({ scheme: 'otzaria', bookId: 12 })
    expect(parseBookLink('otzaria://open/book/12?index=-1')).toEqual({ scheme: 'otzaria', bookId: 12 })
    expect(parseBookLink('otzaria://open/book/12?q=x')).toEqual({ scheme: 'otzaria', bookId: 12 })
    expect(parseBookLink('kitveihakodeshapp://book/7')).toEqual({ scheme: 'kitveihakodeshapp', bookId: 7 })
    expect(parseBookLink('zayit://book/4')).toEqual({ scheme: 'zayit', bookId: 4 })
    expect(parseBookLink('zayit://book/4/')).toEqual({ scheme: 'zayit', bookId: 4 })
  })
  it('keeps Otzaria highlight and source parameters apart', () => {
    expect(parseBookLink(`otzaria://open/book/12?index=3&m=${ENCODED}`)).toEqual({ scheme: 'otzaria', bookId: 12, index: 3 })
    expect(parseBookLink('otzaria://open/book/12?source=user&index=3')).toEqual({ scheme: 'otzaria', bookId: 12, index: 3, otherLibrary: true })
    expect(parseBookLink('otzaria://open/book/12?source=db:my-lib')).toEqual({ scheme: 'otzaria', bookId: 12, otherLibrary: true })
    expect(parseBookLink('otzaria://open/book/12?source=official&index=1')).toEqual({ scheme: 'otzaria', bookId: 12, index: 1 })
  })
  it('opens an Otzaria PDF link at the book (a page is not a line)', () => {
    expect(parseBookLink('otzaria://open/pdf/120?index=17')).toEqual({ scheme: 'otzaria', bookId: 120 })
  })
  it('ignores case of scheme, host and nouns', () => {
    expect(parseBookLink('OTZARIA://Open/Book/12?INDEX=3')).toEqual({ scheme: 'otzaria', bookId: 12, index: 3 })
    expect(parseBookLink('Zayit://BOOK/4/Line/99')).toEqual({ scheme: 'zayit', bookId: 4, lineId: 99 })
    expect(parseBookLink('KitveiHakodeshApp://Book/7?Index=2')).toEqual({ scheme: 'kitveihakodeshapp', bookId: 7, index: 2 })
  })
  it('names a book by title, raw or URL-encoded', () => {
    expect(parseBookLink(`otzaria://open/book/${ENCODED}?index=4`)).toEqual({ scheme: 'otzaria', title: TITLE, index: 4 })
    expect(parseBookLink(`kitveihakodeshapp://book/${TITLE}`)).toEqual({ scheme: 'kitveihakodeshapp', title: TITLE })
    expect(parseBookLink(`zayit://book/${ENCODED}/`)).toEqual({ scheme: 'zayit', title: TITLE })
  })
  it("reads Otzaria's book:// links with a heading path", () => {
    expect(parseBookLink(`book://${TITLE}`)).toEqual({ scheme: 'otzaria', title: TITLE })
    expect(parseBookLink(`book://${ENCODED}#${encodeURIComponent(HEADING)}#x`)).toEqual({ scheme: 'otzaria', title: TITLE, headings: [HEADING, 'x'] })
    expect(parseBookLink(`BOOK://${TITLE} ${TITLE}/`)).toEqual({ scheme: 'otzaria', title: `${TITLE} ${TITLE}` })
  })
  it("reads Otzaria's inline-link (file path, 1-based index)", () => {
    expect(parseBookLink(`otzaria://inline-link?path=${encodeURIComponent(`C:\\lib\\${TITLE}.txt`)}&index=5&ref=x`)).toEqual({ scheme: 'otzaria', title: TITLE, index: 4 })
    expect(parseBookLink(`otzaria://inline-link?path=a/${ENCODED}`)).toEqual({ scheme: 'otzaria', title: TITLE })
  })
  it("reads Zayit's citations and web share page", () => {
    expect(parseBookLink('seforim://line/123')).toEqual({ scheme: 'zayit', lineId: 123 })
    expect(parseBookLink('https://zayitapp.com/open.html#book/4/line/99')).toEqual({ scheme: 'zayit', bookId: 4, lineId: 99 })
    expect(parseBookLink('https://zayitapp.com/open#book/4')).toEqual({ scheme: 'zayit', bookId: 4 })
  })
  it('rejects other links', () => {
    expect(parseBookLink('https://example.com/book/12?index=3')).toBeNull()
    expect(parseBookLink('otzaria://open/calendar')).toBeNull()
    expect(parseBookLink('otzaria://open/search?q=x')).toBeNull()
    expect(parseBookLink('otzaria://open/book/0')).toBeNull()
    expect(parseBookLink('zayit://book/4/line/x')).toBeNull()
    expect(parseBookLink('seforim://line/abc')).toBeNull()
    expect(parseBookLink('book://')).toBeNull()
    expect(parseBookLink('otzaria://inline-link?index=3')).toBeNull()
  })
})

describe('parseSearchLink', () => {
  it('reads Otzaria and Zayit search links', () => {
    expect(parseSearchLink(`otzaria://open/search?q=${ENCODED}&mode=exact`)).toBe(TITLE)
    expect(parseSearchLink(`zayit://search/${ENCODED}`)).toBe(TITLE)
    expect(parseSearchLink('zayit://search/a+b')).toBe('a b')
    expect(parseSearchLink('otzaria://open/search')).toBeNull()
    expect(parseSearchLink('otzaria://open/book/1')).toBeNull()
  })
})

describe('isLibraryAppLink', () => {
  it('knows every library-app scheme, and nothing else', () => {
    for (const url of ['otzaria://open/tools', 'Zayit://x', 'book://a', 'seforim://line/1', 'kitveihakodeshapp://x', 'seforimapp://x', 'https://zayitapp.com/open.html#book/1'])
      expect(isLibraryAppLink(url)).toBe(true)
    for (const url of ['https://example.com', 'mailto:a@b.c', '#bookmark-x', 'file:///c:/x'])
      expect(isLibraryAppLink(url)).toBe(false)
  })
})
