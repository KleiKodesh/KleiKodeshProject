// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { markReadAloudWord } from './readAloudWordMark'
import { splitIntoChunks } from '../useBookViewReadAloud'

const mark = (html: string, letterStart: number, letterCount: number, spokenWord = '') =>
  markReadAloudWord(html, { lineIndex: 0, letterStart, letterCount, spokenWord })

describe('markReadAloudWord', () => {
  it('wraps the word found by its letters', () => {
    expect(mark('אף ד במשה', 3, 4)).toBe('אף ד <mark class="kh-read-aloud">במשה</mark>')
    expect(mark('<b>אף</b>, במשה', 0, 2)).toBe('<b><mark class="kh-read-aloud">אף</mark></b>, במשה')
  })
  it('keeps the HTML well formed when markup is inside the word', () => {
    expect(mark('ב<i>משה</i> אמר', 0, 4)).toBe('<mark class="kh-read-aloud">ב</mark><i><mark class="kh-read-aloud">משה</mark></i> אמר')
  })
  it('writes the pointed spoken word in, its points in unselectable spans', () => {
    expect(mark('ויבא משה', 4, 3, 'מֹשֶׁה.')).toBe('ויבא <mark class="kh-read-aloud">מ<span class="kh-read-aloud-points">ֹ</span>ש<span class="kh-read-aloud-points">ֶׁ</span>ה</mark>')
    // Other letters (an abbreviation read in full): only marked.
    expect(mark('אמר ר\' יהושע', 3, 1, 'רַבִּי')).toBe('אמר <mark class="kh-read-aloud">ר</mark>\' יהושע')
  })
  it('is fast on a long line', () => {
    const line = Array.from({ length: 300 }, () => '<span>ומשה</span> היה רועה').join(' ')
    const t0 = performance.now()
    for (let i = 0; i < 100; i++) mark(line, 800, 4, 'רוֹעֶה')
    expect((performance.now() - t0) / 100).toBeLessThan(5)
  })
})

describe('splitIntoChunks', () => {
  it('cuts a too-long sentence at its last comma within the limit, else at a space', () => {
    const long = `${'א'.repeat(60)}, ${'ב'.repeat(60)} ${'ג'.repeat(60)} ${'ד'.repeat(60)}.`
    const chunks = splitIntoChunks(long, 200)
    expect(chunks[0]!.text.endsWith(',')).toBe(true)
    expect(chunks.map((c) => c.text).join('')).toBe(long)
    const noComma = `${'א'.repeat(150)} ${'ב'.repeat(100)}.`
    expect(splitIntoChunks(noComma, 200)[0]!.text).toBe('א'.repeat(150))
  })
})
