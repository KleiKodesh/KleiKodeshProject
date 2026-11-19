import { describe, it, expect } from 'vitest'
import { buildBookTitleIndex, findTocPathEntry, foldTitle, normalizeHeadingText, pickBook } from './homeDailyLearningBookMatch'

const books = [
  { id: 1, title: 'תהלים', parentPath: 'תנ"ך / כתובים' },
  { id: 2, title: 'מאירי על משנה מידות', parentPath: 'משנה / ראשונים' },
  { id: 3, title: 'משנה מדות', parentPath: 'משנה / סדר קדשים' },
  { id: 4, title: 'משנה מדות', parentPath: 'משנה / אחרונים / פירוש / סדר קדשים' },
  { id: 5, title: 'תלמוד ירושלמי שביעית', parentPath: 'תלמוד ירושלמי / סדר זרעים' },
  { id: 6, title: 'תלמוד ירושלמי שבועות', parentPath: 'תלמוד ירושלמי / סדר נזיקין' },
  { id: 7, title: 'זבחים', parentPath: 'תלמוד בבלי / סדר קדשים' },
  { id: 8, title: 'זבחים', parentPath: 'משנה / סדר קדשים' },
]
const index = buildBookTitleIndex(books)

describe('title folding', () => {
  it('plene and defective spellings meet', () => {
    expect(foldTitle('תהילים')).toBe(foldTitle('תהלים'))
    expect(foldTitle('משנה מקוואות')).toBe(foldTitle('משנה מקואות'))
    expect(foldTitle('משנה תורה, הלכות ביכורים')).toBe(foldTitle('משנה תורה הלכות בכורים'))
  })
  it('maqaf becomes a space, niqqud and geresh go', () => {
    expect(normalizeHeadingText('לֶךְ־לְךָ')).toBe('לך לך')
    expect(normalizeHeadingText('דף ע״ב:')).toBe('דף עב:')
  })
})

describe('pickBook', () => {
  it('finds a book whatever its spelling', () => {
    expect(pickBook(index, { title: 'תהילים', section: 'תנ"ך' })?.id).toBe(1)
  })
  it('prefers the base text over a commentary filed deeper', () => {
    expect(pickBook(index, { title: 'משנה מידות', section: 'משנה' })?.id).toBe(3)
  })
  it('exact spelling wins where two titles fold alike', () => {
    expect(pickBook(index, { title: 'תלמוד ירושלמי שבועות', section: 'תלמוד ירושלמי' })?.id).toBe(6)
    expect(pickBook(index, { title: 'תלמוד ירושלמי שביעית', section: 'תלמוד ירושלמי' })?.id).toBe(5)
  })
  it('keeps to the section', () => {
    expect(pickBook(index, { title: 'זבחים', section: 'תלמוד בבלי' })?.id).toBe(7)
    expect(pickBook(index, { title: 'מידות', section: 'תלמוד בבלי' })).toBeUndefined()
  })
})

describe('findTocPathEntry', () => {
  const toc = [
    { id: 10, parentId: null, text: 'חפץ חיים', lineIndex: 0 },
    { id: 11, parentId: 10, text: 'הקדמה', lineIndex: 2 },
    { id: 12, parentId: 10, text: 'חלק ראשון: הלכות איסורי לשון הרע', lineIndex: 90 },
    { id: 13, parentId: 12, text: 'כלל א', lineIndex: 92 },
    { id: 14, parentId: 13, text: 'סעיף א', lineIndex: 95 },
    { id: 15, parentId: 10, text: 'חלק שני: הלכות איסורי רכילות', lineIndex: 400 },
    { id: 16, parentId: 15, text: 'כלל א', lineIndex: 402 },
    { id: 17, parentId: 16, text: 'סעיף ב', lineIndex: 405 },
  ]
  it('looks for each step under the previous one', () => {
    expect(findTocPathEntry(toc, [['%הלכות איסורי רכילות%'], ['כלל א'], ['סעיף ב']])?.id).toBe(17)
  })
  it('stops at the deepest step found', () => {
    expect(findTocPathEntry(toc, [['%הלכות איסורי רכילות%'], ['כלל א'], ['סעיף ט']])?.id).toBe(16)
    expect(findTocPathEntry(toc, [['פרק א']])).toBeNull()
  })
  it('tells amud bet (colon) from amud alef', () => {
    const daf = [
      { id: 1, parentId: null, text: 'דף ב.', lineIndex: 1 },
      { id: 2, parentId: null, text: 'דף ב:', lineIndex: 5 },
    ]
    expect(findTocPathEntry(daf, [['דף ב:']])?.id).toBe(2)
    expect(findTocPathEntry(daf, [['דף ב', 'דף ב.']])?.id).toBe(1)
  })
  it('folds spelling only in an anySpelling step', () => {
    const parshiyot = [{ id: 1, parentId: null, text: 'בחוקותי', lineIndex: 7 }]
    expect(findTocPathEntry(parshiyot, [['בחקתי']])).toBeNull()
    expect(findTocPathEntry(parshiyot, [{ anySpelling: ['בחקתי'] }])?.id).toBe(1)
  })
  it('numbered headings never fold (כו is not כ)', () => {
    const chapters = [
      { id: 1, parentId: null, text: 'פרק כ', lineIndex: 1 },
      { id: 2, parentId: null, text: 'פרק כו', lineIndex: 9 },
    ]
    expect(findTocPathEntry(chapters, [['פרק כו']])?.id).toBe(2)
  })
})
