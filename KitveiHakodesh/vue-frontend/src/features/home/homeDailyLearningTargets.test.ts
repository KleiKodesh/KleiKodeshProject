import { describe, it, expect } from 'vitest'
import {
  bavliDafTarget,
  chofetzChaimTarget,
  dafStep,
  hebrewLettersToNumber,
  vilnaPageMarkerAnchor,
  yerushalmiDafTarget,
  parseDafYomiString,
  rambamTarget,
  shemiratHalashonTarget,
  tehillimMonthDayTarget,
} from './homeDailyLearningTargets'

describe('daf targets', () => {
  it('parses hebcal daf strings', () => {
    expect(parseDafYomiString('חולין דף יד:')).toEqual({ tractate: 'חולין', dafLetters: 'יד' })
    expect(parseDafYomiString('חולין')).toBeNull()
  })
  it('amud bet looks for the colon heading first', () => {
    expect(dafStep('יז', 'ב')[0]).toBe('דף יז:')
    expect(dafStep('יז')[0]).toBe('דף יז')
  })
  it('tractates outside the Bavli fall back to Yerushalmi, then Mishnah', () => {
    expect(bavliDafTarget('שקלים', 'ב').books.map((book) => book.section)).toEqual([
      'תלמוד בבלי',
      'תלמוד ירושלמי',
      'משנה',
    ])
  })
})

describe('rambam targets', () => {
  it('names the Mishneh Torah book, adding "הלכות" when hebcal leaves it out', () => {
    expect(rambamTarget('עירובין', 'א').books.map((book) => book.title)).toEqual([
      'משנה תורה, עירובין',
      'משנה תורה, הלכות עירובין',
    ])
  })
  it('a reading with no chapter opens the book start', () => {
    expect(rambamTarget('מצוות עשה', null).tocPath).toEqual([])
  })
})

describe('chofetz chaim', () => {
  it('section, then klal, then seif', () => {
    const path = chofetzChaimTarget('HilchosRechilus', 'ז', 'ב').tocPath
    expect(path).toHaveLength(3)
    expect(path[1]).toContain('כלל ז')
    expect(path[2]).toContain('סעיף ב')
  })
  it('the opening sections sit under the Petichah', () => {
    expect(chofetzChaimTarget('Lavin', 'א', null).tocPath).toEqual([['פתיחה%'], ['לאוין']])
  })
})

describe('shemirat halashon', () => {
  it('part one goes through the gate before the chapter', () => {
    const path = shemiratHalashonTarget(1, 'Shar Hazechira', 'יד').tocPath
    expect(path).toHaveLength(3)
    expect(path[1]).toEqual({ anySpelling: ['שער הזכירה', 'שער הזכירה %'] })
  })
  it('part two has chapters right under the part', () => {
    expect(shemiratHalashonTarget(2, 'x', 'א').tocPath).toHaveLength(2)
  })
})

describe('line anchors', () => {
  it('reads Hebrew numerals', () => {
    expect(hebrewLettersToNumber('יט')).toBe(19)
    expect(hebrewLettersToNumber('קכ"ג')).toBe(123)
    expect(hebrewLettersToNumber('abc')).toBe(0)
  })
  it('Vilna page marker accepts both spellings, amud alef first', () => {
    expect(vilnaPageMarkerAnchor('לד').pageMarker!.values).toEqual(['לד.', '34a', 'לד:', '34b'])
    expect(vilnaPageMarkerAnchor('יט', 'ב').pageMarker!.values).toEqual(['יט:', '19b'])
    expect(yerushalmiDafTarget('פאה', 'יט').lineAnchor?.pageMarker?.overlay).toBe('Vilna Pages')
  })
  it('a mitzvah-count reading anchors on the heRef ending', () => {
    expect(rambamTarget('מצוות עשה', 'א', 'א').lineAnchor).toEqual({ heRefSuffix: ', א' })
    expect(rambamTarget('הלכות דעות', 'ב').lineAnchor).toBeUndefined()
  })
})

describe('tehillim by day of month', () => {
  it('targets the 30 Day Cycle alt entry for the day', () => {
    const target = tehillimMonthDayTarget('טו', 'עט')
    expect(target.altTocKey).toBe('30 Day Cycle')
    expect(target.tocPath).toEqual([['יום טו']])
    expect(target.books[0]!.title).toBe('תהילים')
  })
  it('falls back to the first perek heading when the day-cycle alt TOC is missing', () => {
    const target = tehillimMonthDayTarget('טו', 'עט')
    expect(target.fallbackTocPath).toEqual([['פרק עט', 'פרק עט %', 'פרק עט,%']])
  })
})
