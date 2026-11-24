import type { HomeDailyLearningBookName, HomeDailyLearningLineAnchor, HomeDailyLearningTarget, HomeDailyLearningTocStep } from './homeDailyLearningTypes'

/**
 * Library targets for each kind of daily-learning card: which book (by title and library
 * section) and the TOC path inside it. Pure; homeDailyLearningItems.ts feeds it hebcal's
 * readings, already in Hebrew letters.
 */

const BAVLI = 'תלמוד בבלי'
const YERUSHALMI = 'תלמוד ירושלמי'
const MISHNAH = 'משנה'
const TANACH = 'תנ"ך'
const MISHNEH_TORAH = 'הלכה / משנה תורה'

/** TOC step for a heading "<word> <letters>": exact first, then followed by more text. */
export function headingStep(headingWord: string, letters: string): string[] {
  return [`${headingWord} ${letters}`, `${headingWord} ${letters} %`, `${headingWord} ${letters},%`]
}

/**
 * "<tractate> דף <letters>[.:]" (hebcal's daf string) → tractate and daf letters; null when
 * the text has another shape.
 */
export function parseDafYomiString(dafYomi: string): { tractate: string; dafLetters: string } | null {
  const dafIndex = dafYomi.lastIndexOf('דף')
  if (dafIndex === -1) return null
  const tractate = dafYomi.slice(0, dafIndex).trim()
  const dafLetters = dafYomi.slice(dafIndex + 2).trim().replace(/[.:׃]+$/, '').trim()
  return tractate && dafLetters ? { tractate, dafLetters } : null
}

/**
 * TOC step for a daf. The Bavli TOC writes amud alef as "דף X" and amud bet as "דף X:";
 * some editions use "דף X." or "דף X עמוד א".
 */
export function dafStep(dafLetters: string, amud?: 'א' | 'ב'): string[] {
  const daf = `דף ${dafLetters}`
  const amudA = [daf, `${daf}.`, `${daf} עמוד א%`]
  const amudB = [`${daf}:`, `${daf} עמוד ב%`]
  return [...(amud === 'ב' ? [...amudB, ...amudA] : [...amudA, ...amudB]), `${daf} %`]
}

const HEBREW_LETTER_VALUES: Record<string, number> = {
  א: 1, ב: 2, ג: 3, ד: 4, ה: 5, ו: 6, ז: 7, ח: 8, ט: 9, י: 10, כ: 20, ך: 20, ל: 30, מ: 40, ם: 40,
  נ: 50, ן: 50, ס: 60, ע: 70, פ: 80, ף: 80, צ: 90, ץ: 90, ק: 100, ר: 200, ש: 300, ת: 400,
}

/** Hebrew numeral letters → number (geresh/quotes ignored); 0 when not a numeral. */
export function hebrewLettersToNumber(letters: string): number {
  let sum = 0
  for (const ch of letters) {
    const value = HEBREW_LETTER_VALUES[ch]
    if (value) sum += value
    else if (!`'"׳״`.includes(ch)) return 0
  }
  return sum
}

/**
 * The Vilna page marker of a daf in the text, in both spellings the library uses: Hebrew
 * letters with "." / ":" (Mishnah Middot, Kinnim) and Arabic number with a / b (Yerushalmi).
 */
export function vilnaPageMarkerAnchor(dafLetters: string, amud?: 'א' | 'ב'): HomeDailyLearningLineAnchor {
  const dafNumber = hebrewLettersToNumber(dafLetters)
  const amudA = [`${dafLetters}.`, ...(dafNumber ? [`${dafNumber}a`] : [])]
  const amudB = [`${dafLetters}:`, ...(dafNumber ? [`${dafNumber}b`] : [])]
  return { pageMarker: { overlay: 'Vilna Pages', values: amud === 'ב' ? amudB : [...amudA, ...amudB] } }
}

/**
 * A Bavli daf. Tractates the Bavli doesn't cover (Shekalim, Middot, Kinnim, which the daf
 * yomi learns from the Yerushalmi / Mishnah) fall back to those books.
 */
export function bavliDafTarget(tractate: string, dafLetters: string, amud?: 'א' | 'ב'): HomeDailyLearningTarget {
  return {
    books: [
      { title: tractate, section: BAVLI },
      { title: `${YERUSHALMI} ${tractate}`, section: YERUSHALMI },
      { title: `${MISHNAH} ${tractate}`, section: MISHNAH },
    ],
    tocPath: [dafStep(dafLetters, amud)],
    lineAnchor: vilnaPageMarkerAnchor(dafLetters, amud),
  }
}

/** A Yerushalmi daf, by the Vilna edition's alt TOC (the main TOC is perek/halacha). */
export function yerushalmiDafTarget(tractate: string, dafLetters: string): HomeDailyLearningTarget {
  return {
    books: [{ title: `${YERUSHALMI} ${tractate}`, section: YERUSHALMI }],
    tocPath: [dafStep(dafLetters)],
    altTocKey: 'Vilna',
    lineAnchor: vilnaPageMarkerAnchor(dafLetters),
  }
}

/** A Mishnah perek: the Mishnah tractate itself, never a commentary on it. */
export function mishnahPerekTarget(tractate: string, perekLetters: string): HomeDailyLearningTarget {
  return { books: [{ title: `${MISHNAH} ${tractate}`, section: MISHNAH }], tocPath: [headingStep('פרק', perekLetters)] }
}

/** A Tanach book's perek (Nach yomi). */
export function tanachPerekTarget(bookTitle: string, perekLetters: string): HomeDailyLearningTarget {
  return { books: [{ title: bookTitle, section: TANACH }], tocPath: [headingStep('פרק', perekLetters)] }
}

/**
 * Tehillim by day of the Hebrew month: the book's "30 Day Cycle" alt TOC entry "יום <letters>".
 * On day 29 of a 29-day month hebcal reads 140-150; that starts at the day-29 entry too.
 * Editions without that alt TOC open at "פרק <firstChapterLetters>" (the day's first perek).
 */
export function tehillimMonthDayTarget(dayLetters: string, firstChapterLetters: string): HomeDailyLearningTarget {
  return {
    books: [{ title: 'תהילים', section: TANACH }],
    tocPath: [[`יום ${dayLetters}`]],
    altTocKey: '30 Day Cycle',
    fallbackTocPath: [headingStep('פרק', firstChapterLetters)],
  }
}

/** Weekly parsha: the Torah book, at the parsha heading (main TOC or its parsha alt TOC), any spelling. */
export function parshaTarget(torahBookTitle: string, parshaName: string): HomeDailyLearningTarget {
  return {
    books: [{ title: torahBookTitle, section: TANACH }],
    tocPath: [
      {
        anySpelling: [
          `פרשת ${parshaName}`,
          `פרשת ${parshaName} %`,
          parshaName,
          `%${parshaName}%`,
          // Some editions shorten a two-word name to its first word (שלח לך → שלח).
          ...(parshaName.includes(' ') ? [parshaName.split(' ')[0]!] : []),
        ],
      },
    ],
  }
}

/**
 * A Mishneh Torah reading. `name` is hebcal's Hebrew name ("הלכות X", or an intro part such
 * as "מצוות עשה"); the book is "משנה תורה, <name>". `perek` is a chapter's letters, or null
 * when the reading isn't a chapter (the contents list): then the book start. `mitzvahLetters`
 * is the first mitzvah of a mitzvah-count reading: each mitzvah is one line whose heRef ends
 * ", <letters>".
 */
export function rambamTarget(
  name: string,
  perekLetters: string | null,
  mitzvahLetters: string | null = null,
): HomeDailyLearningTarget {
  // hebcal drops "הלכות" from a few names (עירובין); the library title has it.
  const titles = name.startsWith('הלכות') ? [name] : [name, `הלכות ${name}`]
  return {
    books: titles.map((title) => ({ title: `משנה תורה, ${title}`, section: MISHNEH_TORAH })),
    tocPath: perekLetters ? [headingStep('פרק', perekLetters)] : [],
    ...(mitzvahLetters ? { lineAnchor: { heRefSuffix: `, ${mitzvahLetters}` } } : {}),
  }
}

/** Chofetz Chaim section keys (hebcal) → TOC steps down to that section. */
const CHOFETZ_CHAIM_SECTION_PATH: Record<string, string[][]> = {
  Hakdamah: [['הקדמה']],
  Psichah: [['פתיחה%'], ['הקדמה']],
  Lavin: [['פתיחה%'], ['לאוין']],
  Asin: [['פתיחה%'], ['עשין', 'עשיין']],
  Arurin: [['פתיחה%'], ['ארורין']],
  HilchosLH: [['%הלכות איסורי לשון הרע%', 'חלק ראשון%']],
  HilchosRechilus: [['%הלכות איסורי רכילות%', 'חלק שני%']],
  Tziyurim: [['ציורים']],
}

/**
 * Chofetz Chaim: the section, then (in the two halachic parts) "כלל N" under it and
 * "סעיף M" under that; in the illustrations, "ציור N". `klalLetters`/`seifLetters` are
 * hebcal's "klal.seif" start split into letters; null when absent.
 */
export function chofetzChaimTarget(
  sectionKey: string,
  firstLetters: string | null,
  secondLetters: string | null,
): HomeDailyLearningTarget {
  const sectionPath = CHOFETZ_CHAIM_SECTION_PATH[sectionKey] ?? []
  const halachic = sectionKey === 'HilchosLH' || sectionKey === 'HilchosRechilus'
  const deeper: HomeDailyLearningTocStep[] = halachic
    ? [
        ...(firstLetters ? [headingStep('כלל', firstLetters)] : []),
        ...(firstLetters && secondLetters ? [headingStep('סעיף', secondLetters)] : []),
      ]
    : sectionKey === 'Tziyurim' && firstLetters
      ? [headingStep('ציור', firstLetters)]
      : []
  return { books: [{ title: 'חפץ חיים' }], tocPath: [...sectionPath, ...deeper] }
}

/** Shemirat HaLashon gate keys (hebcal) → TOC heading. */
const SHEMIRAT_HALASHON_GATE: Record<string, string> = {
  Hakdamah: 'הקדמה',
  'Shar Hazechira': 'שער הזכירה',
  'Shar Hatvuna': 'שער התבונה',
  'Shar Hatorah': 'שער התורה',
  'Chasimas Hasefer': 'חתימת הספר',
}

export function shemiratHalashonGateName(gateKey: string): string | undefined {
  return SHEMIRAT_HALASHON_GATE[gateKey]
}

/**
 * Shemirat HaLashon: the part ("חלק ראשון"/"חלק שני"), then the gate in part one (chapters
 * restart in every gate), then the chapter. Part two has chapters directly.
 */
export function shemiratHalashonTarget(
  partNumber: number,
  gateKey: string,
  chapterLetters: string | null,
): HomeDailyLearningTarget {
  const part = partNumber === 2 ? 'חלק שני' : 'חלק ראשון'
  const gate = SHEMIRAT_HALASHON_GATE[gateKey]
  const chapterStep = chapterLetters && gate !== 'הקדמה' && gate !== 'חתימת הספר' ? [headingStep('פרק', chapterLetters)] : []
  return {
    books: [{ title: 'שמירת הלשון' }],
    tocPath: [[part, `${part} %`, `${part}:%`], ...(gate ? [{ anySpelling: [gate, `${gate} %`] }] : []), ...chapterStep],
  }
}

/** A siman of a book whose TOC is "סימן N" (Kitzur Shulchan Aruch). */
export function simanTarget(book: HomeDailyLearningBookName, simanLetters: string | null): HomeDailyLearningTarget {
  return { books: [book], tocPath: simanLetters ? [headingStep('סימן', simanLetters)] : [] }
}
