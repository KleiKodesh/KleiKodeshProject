import { describe, expect, it } from 'vitest'
import type { TocEntry } from '@/webview-host/queries.types'
import {
  altTocBreakSources,
  altTocLabelNumber,
  buildAltTocLineLabels,
  isAltTocLabelVisible,
  lineStartsWithNumber,
  printsOwnNumbering,
  sampleNumberedLeaves,
} from './altTocTextLabels'

const isHebrewNumeralLabel = (word: string) => altTocLabelNumber(word) != null

let nextId = 1
function entry(text: string, lineIndex: number, level: number, hasChildren = false): TocEntry {
  const id = nextId++
  return { id, parentId: null, level, hasChildren, text, lineId: 1000 + lineIndex, lineIndex }
}

describe('buildAltTocLineLabels', () => {
  it('breaks on parents and names, inlines counters, across structures', () => {
    const map = buildAltTocLineLabels(
      [
        { structure: { id: 1 }, entries: [entry('פסוק א', 5, 1), entry('פרשת נח', 5, 0, true)] },
        { structure: { id: 2 }, entries: [entry('דף ב.', 5, 1)] },
        { structure: { id: 3 }, entries: [entry('הלכות ציצית', 5, 0)] },
      ],
      new Set(),
    )
    expect(map.get(5)).toEqual({ breaks: ['פרשת נח', 'הלכות ציצית'], inlines: ['פסוק א', 'דף ב.'] })
  })

  it('inlines a bare numeral only on a level of counters ("נח" is a parasha)', () => {
    const letters = ['א', 'ב', 'ג', 'ד', 'ה'].map((l, i) => entry(l, 10 + i, 1))
    const parashot = [entry('בראשית', 20, 0), entry('נח', 21, 0), entry('לך לך', 22, 0)]
    const map = buildAltTocLineLabels(
      [{ structure: { id: 4 }, entries: letters }, { structure: { id: 5 }, entries: parashot }],
      new Set(),
    )
    expect(map.get(10)).toEqual({ breaks: [], inlines: ['א'] })
    expect(map.get(21)).toEqual({ breaks: ['נח'], inlines: [] })
  })

  it('breaks on names ending in a numeral-shaped word ("המביא קמא")', () => {
    const perakim = [entry('המביא קמא', 30, 0), entry('כל הגט', 31, 0), entry('המביא תניין', 32, 0)]
    const map = buildAltTocLineLabels([{ structure: { id: 6 }, entries: perakim }], new Set())
    expect(map.get(30)).toEqual({ breaks: ['המביא קמא'], inlines: [] })
  })

  it('inlines aliyot under a parasha', () => {
    const entries = [entry('בראשית', 40, 0, true), entry('ראשון', 40, 1), entry('שני', 44, 1), entry('שלישי', 48, 1)]
    const map = buildAltTocLineLabels([{ structure: { id: 7 }, entries }], new Set())
    expect(map.get(40)).toEqual({ breaks: ['בראשית'], inlines: ['ראשון'] })
    expect(map.get(44)).toEqual({ breaks: [], inlines: ['שני'] })
  })

  it('skips a structure whose book prints its own numbering', () => {
    const map = buildAltTocLineLabels([{ structure: { id: 7 }, entries: [entry('סעיף א', 3, 1)] }], new Set([7]))
    expect(map.size).toBe(0)
  })
})

describe('altTocBreakSources', () => {
  it('lifts a section title over the siman heading right above its line', () => {
    const lines = ['טקסט', '<h2>סימן תרע</h2>', 'בבית שני', 'וגמר הנס']
    const at = (i: number) => lines[i] ?? null
    expect(altTocBreakSources(1, at)).toEqual([1, 2]) // "הלכות חנוכה" of line 2 draws above the heading
    expect(altTocBreakSources(2, at)).toEqual([]) // ...and not again on its own line
    expect(altTocBreakSources(3, at)).toEqual([3])
  })

  it('never climbs over a higher-rank heading ("אורח חיים" above "סימן א")', () => {
    const lines = ['הקדמה', '<h2>אורח חיים</h2>', '<h3>סימן א</h3>', 'כתיב']
    const at = (i: number) => lines[i] ?? null
    expect(altTocBreakSources(2, at)).toEqual([2, 3]) // "הלכות השכמת הבוקר" between the two headings
    expect(altTocBreakSources(1, at)).toEqual([1])
  })
})

describe('isAltTocLabelVisible', () => {
  it('finds a label at the start of its own line', () => {
    expect(isAltTocLabelVisible('הלכות ציצית', ['<b>הלכות ציצית</b>', null, null])).toBe(true)
    expect(isAltTocLabelVisible('הלכות ציצית', ['הלכות ציצית: ומה שבתוכו', null, null])).toBe(true)
  })

  it('finds a label one line off', () => {
    expect(isAltTocLabelVisible('הלכות ציצית', ['ואף על גב', '<h3>הלכות ציצית</h3>', null])).toBe(true)
  })

  it('treats a short heading that opens the label as visible ("[סימן קצז] דיני..." on "סימן קצז")', () => {
    expect(isAltTocLabelVisible('[סימן קצז] דיני צירוף', ['<h2>סימן קצז</h2>'])).toBe(true)
  })

  it('does not drop a seif label on a siman heading line', () => {
    // The first seif of a siman can point at the siman's heading line.
    expect(isAltTocLabelVisible('סעיף א', ['<h2>סימן תרע</h2>', 'שלוש עשרה', 'והאמת כן הוא'])).toBe(false)
  })

  it('does not take a perek\'s opening words for its title (Brachot "מאימתי")', () => {
    expect(isAltTocLabelVisible('מאימתי', ['<big><strong>מֵאֵימָתַי</strong></big> קוֹרִין אֶת שְׁמַע'])).toBe(false)
    expect(isAltTocLabelVisible('היה קורא', ['מַתְנִי׳ <big><strong>הָיָה קוֹרֵא</strong></big> בַּתּוֹרָה'])).toBe(false)
  })

  it('takes a printed marker opening a line of text', () => {
    expect(isAltTocLabelVisible('אות יא', ['[אות יא] בהרע"ב ד"ה מותר'])).toBe(true)
    expect(isAltTocLabelVisible('דף ב.', ['דף ב. גמרא'])).toBe(true)
  })

  it('takes a title on an all-bold line, wrappers aside', () => {
    expect(isAltTocLabelVisible('הלכות ציצית', ['<br><big><b>הלכות ציצית</b></big>'])).toBe(true)
  })

  it('keeps yod in numerals ("דף י." on a "דף י." heading)', () => {
    expect(isAltTocLabelVisible('דף י.', ['<h3>דף י.</h3>'])).toBe(true)
  })

  it('judges very short labels only against a line that is the label alone', () => {
    expect(isAltTocLabelVisible('א', ['(א) אמר רבי'])).toBe(false)
    expect(isAltTocLabelVisible('ב.', ['<h3>ב.</h3>'])).toBe(true)
  })

  it('takes a label held in a bracketed marker opening the line', () => {
    expect(isAltTocLabelVisible('דף לא', ['[דף לא טור א] גרסינן'])).toBe(true)
    expect(isAltTocLabelVisible('סעיף ה', [`(סעיף ה' בהג"ה) ומי`])).toBe(true)
    expect(isAltTocLabelVisible('אות קיא ב', ['[אות קיא] ב במשנה'])).toBe(true)
    // Another number in the marker is no match.
    expect(isAltTocLabelVisible('דף ט', ['[דף ו טור א] גרסינן'])).toBe(false)
  })

  it('does not match a label in the middle of the text', () => {
    expect(isAltTocLabelVisible('סעיף ד', ['ואף על גב שהנס לא היה כמו שכתוב בסעיף ד'])).toBe(false)
  })
})

describe('printed numbering', () => {
  it('accepts the printed forms of a number', () => {
    for (const line of ['(ד) ואף', '[ד] ואף', 'ד. ואף', 'ד) ואף', 'ד: ואף', '<b>(ד)</b> ואף', 'ד ואף']) {
      expect(lineStartsWithNumber(line, 'ד'), line).toBe(true)
    }
    expect(lineStartsWithNumber('דבמדרש איתא', 'ד')).toBe(false)
  })

  it('needs most sampled leaves to match', () => {
    const leaves = Array.from({ length: 10 }, (_, i) => entry(`סעיף ${'אבגדהוזחטי'[i]}`, i, 1))
    const sample = sampleNumberedLeaves(leaves)
    const printed = new Map(leaves.map((e, i) => [e.lineId!, `(${'אבגדהוזחטי'[i]}) טקסט`]))
    expect(printsOwnNumbering(sample, printed)).toBe(true)
    const half = new Map(leaves.map((e, i) => [e.lineId!, i % 2 ? 'טקסט' : `(${'אבגדהוזחטי'[i]}) טקסט`]))
    expect(printsOwnNumbering(sample, half)).toBe(false)
  })

  it('is no numbering when few leaves are numerals ("נח" among parashot)', () => {
    const parashot = ['בראשית', 'נח', 'לך לך', 'וירא', 'חיי שרה', 'תולדות', 'ויצא', 'וישלח', 'וישב', 'מקץ', 'ויגש']
    expect(sampleNumberedLeaves(parashot.map((p, i) => entry(p, i, 0)))).toEqual([])
  })

  it('does not take a word for a numeral', () => {
    expect(isHebrewNumeralLabel('ויצא')).toBe(false)
    expect(isHebrewNumeralLabel('קצז')).toBe(true)
    expect(isHebrewNumeralLabel('טו')).toBe(true)
  })
})
