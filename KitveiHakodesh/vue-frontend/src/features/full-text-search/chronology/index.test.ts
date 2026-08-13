import { describe, it, expect } from 'vitest'
import { chronologicalKey, authorYear } from './index'

// The chronological sort is author-year-primary: a curated author death year decides a
// book's position, and only three category labels (ראשונים / אחרונים / מחברי זמננו) act as
// a fallback because only those genuinely encode an era. Everything else — topical roots,
// "מפרשים על X" buckets, the 'אחר' default — is era-UNKNOWN and sorts last.
//
// This replaced an era-rank-primary design that read a book's shelf category as its date.
// That misplaced books by centuries (live-verified 2026-08-13): the תנ"ך root bucket held
// 23 books of which exactly ONE was biblical — the rest were 16th-20th c. sermon
// collections — and a single fallback rank absorbed 20 distinct labels spanning 1250-1897.
// The tests below pin the new contract and the failures that motivated it.

const YEAR_UNKNOWN = 999999

// A known author from authorYears.ts, used to assert the author path wins over category.
const LATE_AUTHOR = 'יהודה אריה ליב אלתר' // d. 1905
const EARLY_AUTHOR = "ר' משה הדרשן" // d. 1050

describe('chronologicalKey — author year is the primary key', () => {
  it('uses the author death year regardless of which shelf the book sits on', () => {
    // Same author, three unrelated shelves — the year must not move.
    for (const period of ['חסידות', 'קבלה', 'הלכה', 'תנ"ך', null]) {
      expect(chronologicalKey({ period, authors: LATE_AUTHOR }).year).toBe(1905)
    }
  })

  it('marks an exact author year as the most precise tier', () => {
    expect(chronologicalKey({ period: null, authors: LATE_AUTHOR }).precision).toBe(0)
  })

  it('REGRESSION: a dated book under a topical/biblical root sorts by its real year', () => {
    // The old code capped author years per era rank (ERA_MAX_AUTHOR_YEAR), which zeroed out
    // EVERY author year in the תנ"ך bucket — so 22 early-modern sermon collections shelved
    // under the Bible sorted at the very top of the list, ahead of the Bible itself.
    const shelvedUnderBible = chronologicalKey({ period: 'תנ"ך', authors: LATE_AUTHOR })
    const anonymousBiblical = chronologicalKey({ period: 'תנ"ך', authors: null })
    expect(shelvedUnderBible.year).toBe(1905)
    // The dated sermon collection now carries its real year; the anonymous biblical text
    // has no author and no trusted era label, so it is era-unknown and trails. Both are
    // improvements on the old behaviour, where the whole bucket collapsed to rank 10.
    expect(shelvedUnderBible.year).toBeLessThan(anonymousBiblical.year)
    expect(anonymousBiblical.year).toBe(YEAR_UNKNOWN)
  })

  it('takes the EARLIEST year of a comma-joined author list (base author, not annotator)', () => {
    expect(chronologicalKey({ period: 'מדרש', authors: `${LATE_AUTHOR}, ${EARLY_AUTHOR}` }).year).toBe(
      1050,
    )
  })

  it('normalizes nikud, gershayim, and directional marks in author names', () => {
    expect(chronologicalKey({ period: null, authors: 'יְהוּדָה אַרְיֵה לֵיב אַלְתֵּר' }).year).toBe(1905)
    expect(authorYear('‎יהודה אריה ליב אלתר‏')).toBe(1905)
  })
})

describe('chronologicalKey — era fallback, three trusted labels only', () => {
  it('orders the three era labels chronologically', () => {
    const y = (period: string) => chronologicalKey({ period, authors: null }).year
    expect(y('ראשונים')).toBeLessThan(y('אחרונים'))
    expect(y('אחרונים')).toBeLessThan(y('מחברי זמננו'))
    expect(y('מחברי זמננו')).toBeLessThan(YEAR_UNKNOWN)
  })

  it('marks an era estimate as less precise than an exact year, so exact leads on a tie', () => {
    const era = chronologicalKey({ period: 'אחרונים', authors: null })
    expect(era.precision).toBe(1)
    const exact = chronologicalKey({ period: null, authors: LATE_AUTHOR })
    expect(exact.precision).toBeLessThan(era.precision)
  })

  it('REGRESSION: topical and commentary labels are era-UNKNOWN, not a middle bucket', () => {
    // The old ERA_RANK_FALLBACK=70 pinned 20 distinct labels to one rank between ראשונים
    // and אחרונים. Measured against composition dates that bucket actually spanned
    // 1250-1897: 14% of it predated the ראשונים median and 25% postdated the אחרונים
    // median. It was never a coherent position, so these no longer get one.
    for (const label of [
      'הלכה',
      'קבלה',
      'שו"ת',
      'מדרש',
      'תלמוד בבלי',
      'תלמוד ירושלמי',
      'תנ"ך',
      'חסידות',
      'חברותא',
      'מפרשים על משנה תורה',
      'אחר', // the default booksDataStore assigns when findCategoryMeta returns null
    ]) {
      expect(chronologicalKey({ period: label, authors: null }).year).toBe(YEAR_UNKNOWN)
    }
  })
})

describe('chronologicalKey — unknowns sort last', () => {
  it('gives a finite sentinel year to books with no author year and no era', () => {
    const unknown = chronologicalKey({ period: null, authors: null })
    expect(unknown.year).toBe(YEAR_UNKNOWN)
    expect(unknown.precision).toBe(2)
    // Finite, NOT Infinity: the comparator subtracts these and Infinity-Infinity is NaN.
    expect(Number.isFinite(unknown.year)).toBe(true)
  })

  it('treats an allBooksMap miss the same as a fully unknown book', () => {
    expect(chronologicalKey(undefined)).toEqual(chronologicalKey({ period: null, authors: null }))
  })

  it('sorts unknowns after every dated and era-bucketed book', () => {
    const unknown = chronologicalKey({ period: 'הלכה', authors: null }).year
    expect(unknown).toBeGreaterThan(chronologicalKey({ period: null, authors: EARLY_AUTHOR }).year)
    expect(unknown).toBeGreaterThan(chronologicalKey({ period: null, authors: LATE_AUTHOR }).year)
    expect(unknown).toBeGreaterThan(chronologicalKey({ period: 'מחברי זמננו', authors: null }).year)
  })

  it('never produces NaN when two unknowns are compared', () => {
    const a = chronologicalKey({ period: null, authors: null })
    const b = chronologicalKey(undefined)
    expect(Number.isNaN(a.year - b.year)).toBe(false)
    expect(Number.isNaN(a.precision - b.precision)).toBe(false)
  })
})

describe('chronologicalKey — canonical anonymous works (traditional dating)', () => {
  const y = (title: string) => chronologicalKey({ period: null, authors: null, title }).year

  it('dates the Written Torah to Sinai, ahead of everything else', () => {
    expect(y('בראשית')).toBe(-1312)
    expect(y('בראשית')).toBeLessThan(y('משנה ברכות'))
  })

  it('orders the classic strata in their traditional sequence', () => {
    // Mishnah (Rebbi) → Tosefta → Yerushalmi → Bavli. Note the corpus stores Bavli tractates
    // under their bare name while Yerushalmi tractates carry an explicit prefix — the table
    // is keyed on the stored titles, so both shapes must resolve.
    expect(y('משנה ברכות')).toBeLessThan(y('תוספתא ברכות'))
    expect(y('תוספתא ברכות')).toBeLessThan(y('תלמוד ירושלמי בבא בתרא'))
    expect(y('תלמוד ירושלמי בבא בתרא')).toBeLessThan(y('בבא בתרא'))
  })

  it('follows TRADITIONAL attribution, not critical dating', () => {
    // Targum Yonatan on the Prophets is Yonatan ben Uziel, Hillel's student (Megillah 3a) —
    // ~50 CE, not the 4th-5th century of critical scholarship. It must therefore precede the
    // Mishnah, and precede Pseudo-Jonathan on the Torah, which tradition does NOT ascribe
    // to him.
    expect(y('ישעיהו')).toBeLessThan(y('משנה ברכות'))
    expect(y('תרגום יונתן על יחזקאל')).toBeLessThan(y('משנה ברכות'))
    expect(y('תרגום יונתן על יחזקאל')).toBeLessThan(y('תרגום יונתן על דברים'))
  })

  it('REGRESSION: dates Midrash Rabbah per book, never as one series', () => {
    // Bereshit Rabbah is amoraic; Bemidbar Rabbah is medieval. One series-wide date would
    // misplace one end by ~700 years.
    expect(y('בראשית רבה')).toBeLessThan(y('במדבר רבה'))
    expect(y('במדבר רבה') - y('בראשית רבה')).toBeGreaterThan(500)
  })

  it('REGRESSION: a late anthology OF midrash does not sort with classical midrash', () => {
    // Yalkut Shimoni is 13th-century. Dated as "midrash" it would sort a millennium early.
    expect(y('ילקוט שמעוני על התורה')).toBeGreaterThan(y('בראשית רבה'))
    expect(y('ילקוט שמעוני על התורה')).toBeGreaterThan(y('בבא בתרא'))
  })

  it('counts a canonical date as exact, not an estimate', () => {
    expect(chronologicalKey({ period: null, authors: null, title: 'משנה ברכות' }).precision).toBe(0)
  })

  it('leaves liturgy and modern anthologies undated on purpose', () => {
    // A printed siddur is stratified across two millennia; Batei Midrashot is a modern
    // anthology of dozens of independent works. Both sort last rather than assert a year.
    expect(y('בתי מדרשות -מדרשים קצרים')).toBe(YEAR_UNKNOWN)
    expect(y('עין יעקב (מאת שמואל צבי גליק)')).toBe(YEAR_UNKNOWN)
  })

  it('lets a known author outrank the canonical table', () => {
    // If a book somehow carries both, the author year wins — it is the more specific signal.
    const key = chronologicalKey({ period: null, authors: LATE_AUTHOR, title: 'משנה ברכות' })
    expect(key.year).toBe(1905)
  })
})

describe('chronologicalKey — multi-volume works dated by path stem', () => {
  it('dates every volume of a per-tractate work from its path', () => {
    // Each volume carries a different title (the tractate), so only the path can match.
    const a = chronologicalKey({
      period: null,
      authors: null,
      title: 'ברכות',
      parentPath: 'תלמוד / ירושלמי / מפרשים / נועם ירושלמי / סדר זרעים',
    })
    const b = chronologicalKey({
      period: null,
      authors: null,
      title: 'שבת',
      parentPath: 'תלמוד ירושלמי / מפרשים / נועם ירושלמי / סדר מועד',
    })
    // Both path shapes (nested and flattened) must resolve to the same work.
    expect(a.year).toBe(1873)
    expect(b.year).toBe(1873)
    expect(a.precision).toBe(0)
  })

  it('does not let a stem override a known author', () => {
    const key = chronologicalKey({
      period: null,
      authors: LATE_AUTHOR,
      title: 'ברכות',
      parentPath: 'תלמוד / ירושלמי / מפרשים / נועם ירושלמי',
    })
    expect(key.year).toBe(1905)
  })

  it('leaves the works whose author could not be identified unknown', () => {
    // Yad David's author is unidentified — the title collides with two unrelated Talmud
    // commentaries, so borrowing either one's year would be a fabrication.
    expect(
      chronologicalKey({
        period: null,
        authors: null,
        title: 'ספר המדע',
        parentPath: 'הלכה / משנה תורה / מפרשים / יד דוד',
      }).year,
    ).toBe(YEAR_UNKNOWN)
  })

  it('dates Kikar LaAden by its publisher, as a deliberate exception', () => {
    // The Chida (d. 1806) printed these glosses and credited an unnamed earlier author, so
    // 1806 is the edition's year, not the composition's. Accepted anyway: it places ~40
    // volumes far closer than stranding them at the end of the list.
    const key = chronologicalKey({
      period: null,
      authors: null,
      title: 'ברכות',
      parentPath: 'תלמוד / ירושלמי / מפרשים / ככר לאדן',
    })
    expect(key.year).toBe(1806)
    // Still must beat the title collision with the Bavli tractate of the same name.
    expect(key.year).not.toBe(500)
  })
})

describe('authorYear', () => {
  it('returns null for empty, missing, and unmapped authors', () => {
    expect(authorYear(null)).toBeNull()
    expect(authorYear(undefined)).toBeNull()
    expect(authorYear('')).toBeNull()
    expect(authorYear('מחבר שאינו במפה')).toBeNull()
  })

  it('resolves the newly web-sourced authors added in the 2026-08-13 expansion', () => {
    // Spot-check that the merge (373 -> 574 entries) is actually wired in.
    expect(authorYear('משה סופר')).toBe(1839) // Chatam Sofer
    // normAuthor strips the geresh/apostrophe, so the key matches with or without it.
    expect(authorYear('חיים בן יצחק מוולוזין')).toBe(1821)
  })
})
