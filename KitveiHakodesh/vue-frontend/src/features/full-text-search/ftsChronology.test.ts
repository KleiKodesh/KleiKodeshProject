import { describe, it, expect } from 'vitest'
import { chronologicalKey } from './ftsChronology'

// The period labels used here are the REAL labels the live catalog produces (verified
// against the dev DB on 2026-07-28): findCategoryMeta returns raw ROOT category titles
// (תלמוד בבלי / תלמוד ירושלמי / בית שני), never the keyword form תלמוד, plus topic-only
// roots (הלכה / קבלה) and explicit bucket labels (חברותא / מפרשים על …).
// Author names come from authorYears.json (curated map, keyed by normalized name).

const rank = (period: string | null) => chronologicalKey({ period, authors: null }).rank

describe('chronologicalKey — era rank of the real catalog labels', () => {
  it('orders the classic strata: תנ"ך → בית שני → משנה → תוספתא → ירושלמי → בבלי → מדרש', () => {
    const strata = ['תנ"ך', 'בית שני', 'משנה', 'תוספתא', 'תלמוד ירושלמי', 'תלמוד בבלי', 'מדרש']
    const ranks = strata.map(rank)
    expect([...ranks].sort((a, b) => a - b)).toEqual(ranks)
    expect(new Set(ranks).size).toBe(ranks.length) // all distinct buckets
  })

  it('REGRESSION: the Gemara sorts before the ראשונים (root-title labels must be ranked)', () => {
    // The original bug: 'תלמוד בבלי'/'תלמוד ירושלמי' were missing from ERA_RANK, so the
    // Talmud fell into the topical middle bucket and sorted AFTER the ראשונים.
    expect(rank('תלמוד בבלי')).toBeLessThan(rank('ראשונים'))
    expect(rank('תלמוד ירושלמי')).toBeLessThan(rank('ראשונים'))
  })

  it('places topic-only labels (הלכה/קבלה/מפרשים על …) between ראשונים and אחרונים', () => {
    for (const topic of ['הלכה', 'קבלה', 'שו"ת', 'מפרשים על משנה תורה', 'סדר התפילה', 'אחר']) {
      expect(rank(topic)).toBeGreaterThan(rank('ראשונים'))
      expect(rank(topic)).toBeLessThan(rank('אחרונים'))
    }
  })

  it('places חברותא (contemporary elucidation) after אחרונים and חסידות', () => {
    expect(rank('חברותא')).toBeGreaterThan(rank('אחרונים'))
    expect(rank('חברותא')).toBeGreaterThan(rank('חסידות'))
    expect(rank('חברותא')).toBeLessThan(rank('מחברי זמננו'))
  })

  it('accepts both gershayim variants of תנ"ך', () => {
    expect(rank('תנ"ך')).toBe(rank('תנ״ך'))
  })

  it('sorts a book with an unknown period label to the topical middle, and a missing book to the very end', () => {
    // In-catalog books always carry SOME label (booksDataStore falls back to 'אחר'), so
    // unknown labels land mid-list. Only a book absent from allBooksMap sorts last.
    expect(rank('קטגוריה שאינה קיימת')).toBeLessThan(rank('אחרונים'))
    const missing = chronologicalKey(undefined)
    expect(missing.rank).toBeGreaterThan(rank('מחברי זמננו'))
    expect(missing.year).toBe(Number.POSITIVE_INFINITY)
    expect(chronologicalKey({ period: null, authors: null }).rank).toBe(missing.rank)
  })
})

describe('chronologicalKey — author death-year refinement', () => {
  it('resolves a known author year within a plausible era', () => {
    expect(chronologicalKey({ period: 'חסידות', authors: 'יהודה אריה ליב אלתר' }).year).toBe(1905)
    expect(chronologicalKey({ period: 'מדרש', authors: "ר' משה הדרשן" }).year).toBe(1050)
  })

  it('returns Infinity for missing/unknown authors so undated books trail dated era peers', () => {
    expect(chronologicalKey({ period: 'אחרונים', authors: null }).year).toBe(Number.POSITIVE_INFINITY)
    expect(chronologicalKey({ period: 'אחרונים', authors: '' }).year).toBe(Number.POSITIVE_INFINITY)
    expect(chronologicalKey({ period: 'אחרונים', authors: 'מחבר שאינו במפה' }).year).toBe(
      Number.POSITIVE_INFINITY,
    )
  })

  it('REGRESSION: treats an era-implausible year as unknown (editor/compiler, not author)', () => {
    // אוצר מדרשים is filed under מדרש but lists its 20th-century compiler אייזנשטיין
    // (d. 1956) — before the cap it sorted ahead of undated ancient midrashim.
    expect(chronologicalKey({ period: 'מדרש', authors: 'יהודה דוד אייזנשטיין' }).year).toBe(
      Number.POSITIVE_INFINITY,
    )
    // Any "author" year on a תנ"ך book is an editor's.
    expect(chronologicalKey({ period: 'תנ"ך', authors: "ר' משה הדרשן" }).year).toBe(
      Number.POSITIVE_INFINITY,
    )
  })

  it('keeps the year uncapped in topic buckets and the modern strata — there it IS the chronology', () => {
    expect(chronologicalKey({ period: 'קבלה', authors: 'יהודה לייב הלוי אשלג' }).year).toBe(1954)
    expect(chronologicalKey({ period: 'חסידות', authors: 'יהודה אריה ליב אלתר' }).year).toBe(1905)
  })

  it('takes the EARLIEST year of a comma-joined author list (base author, not annotator)', () => {
    const key = chronologicalKey({
      period: 'מדרש',
      authors: "יהודה דוד אייזנשטיין, ר' משה הדרשן",
    })
    expect(key.year).toBe(1050)
  })

  it('normalizes nikud, gershayim, and directional marks in author names', () => {
    // Same author as 1905 above, decorated the way DB strings sometimes arrive.
    expect(
      chronologicalKey({ period: 'חסידות', authors: '‏יהוּדה אריה ליב אלתר‎' }).year,
    ).toBe(1905)
  })
})
