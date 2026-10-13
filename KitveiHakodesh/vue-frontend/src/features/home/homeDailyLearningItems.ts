import { parseDafYomiString } from './dafYomiNavigation'
import type { HomeDailyLearningItem } from './homeDailyLearningTypes'

/**
 * Builds today's daily-learning items from @hebcal/learning + the weekly parsha
 * from @hebcal/core. Each builder is independent: one that throws or has no
 * reading today just yields no card. Book lookup happens later, by name, in
 * homeDailyLearningResolver.ts.
 */

const GERESH_PATTERN = /[׳״'"]/g
const NIQQUD_PATTERN = /[֑-ֽֿ-ׇ]/g

function cleanHebrew(text: string): string {
  return text.replace(NIQQUD_PATTERN, '').replace(GERESH_PATTERN, '').trim()
}

/** TOC patterns for a heading "<word> <letters>" — exact first, then followed by more text. */
function headingTocPatterns(headingWord: string, letters: string): string[] {
  return [`${headingWord} ${letters}`, `${headingWord} ${letters} %`, `${headingWord} ${letters},%`]
}

/** TOC patterns for a daf "דף <letters>", optionally narrowed to an amud first. */
function dafTocPatterns(dafLetters: string, amudLetter?: string): string[] {
  const amudPatterns = amudLetter ? [`דף ${dafLetters} עמוד ${amudLetter}%`] : []
  return [...amudPatterns, `דף ${dafLetters} %`, `דף ${dafLetters}`, `דף ${dafLetters}%`]
}

/** Torah book exact title for a 1-based parsha number from hebcal's sedra lookup. */
function torahBookTitleForParshaNumber(parshaNumber: number): string {
  if (parshaNumber <= 12) return 'בראשית'
  if (parshaNumber <= 23) return 'שמות'
  if (parshaNumber <= 33) return 'ויקרא'
  if (parshaNumber <= 43) return 'במדבר'
  return 'דברים'
}

const CHOFETZ_CHAIM_SECTION_HEBREW: Record<string, string> = {
  HilchosLH: 'הלכות לשון הרע',
  HilchosRechilus: 'הלכות רכילות',
}

export async function computeHomeDailyLearningItems(today: Date): Promise<HomeDailyLearningItem[]> {
  const [{ HDate, gematriya, getSedra, Locale }, learning, { TRACTATE_HE }] = await Promise.all([
    import('@hebcal/core'),
    import('@hebcal/learning'),
    import('../hebrew-calendar/hebrewCalendarLearning'),
  ])
  const hebrewDate = new HDate(today)
  const toLetters = (value: number) => cleanHebrew(gematriya(value))
  const items: HomeDailyLearningItem[] = []
  const tryAdd = (build: () => HomeDailyLearningItem | null) => {
    try {
      const item = build()
      if (item) items.push(item)
    } catch {}
  }

  tryAdd(() => {
    const sedra = getSedra(hebrewDate.getFullYear(), true).lookup(hebrewDate)
    const parshaNumber = Array.isArray(sedra.num) ? sedra.num[0] : sedra.num
    if (sedra.chag || !sedra.parsha[0] || !parshaNumber) return null
    const parshaNames = sedra.parsha.map((name) => cleanHebrew(Locale.gettext(name, 'he')))
    const firstParshaName = parshaNames[0]!
    return {
      key: 'weekly-parsha',
      label: 'פרשת השבוע',
      location: parshaNames.join('-'),
      target: {
        bookQueries: [torahBookTitleForParshaNumber(parshaNumber)],
        matchTitleExactly: true,
        tocPatterns: [`פרשת ${firstParshaName}`, `פרשת ${firstParshaName} %`, `%${firstParshaName}%`],
      },
    }
  })

  tryAdd(() => {
    // lookup → { k: English book, v: chapter }; render('he') → "<Hebrew book> <letters>".
    const reading = new learning.NachYomiIndex().lookup(hebrewDate) as { k: string; v: number }
    const rendered = cleanHebrew(new learning.NachYomiEvent(hebrewDate, reading).render('he'))
    const lastSpace = rendered.lastIndexOf(' ')
    if (lastSpace <= 0) return null
    const bookName = rendered.slice(0, lastSpace)
    const chapterLetters = toLetters(reading.v)
    return {
      key: 'nach-yomi',
      label: 'נ"ך יומי',
      location: `${bookName} ${chapterLetters}`,
      target: {
        bookQueries: [bookName],
        matchTitleExactly: true,
        tocPatterns: headingTocPatterns('פרק', chapterLetters),
      },
    }
  })

  tryAdd(() => {
    // dailyPsalms → [firstChapter, lastChapter] by day of the Hebrew month.
    const [firstChapter, lastChapter] = learning.dailyPsalms(hebrewDate) as [number, number]
    const firstLetters = toLetters(firstChapter)
    return {
      key: 'daily-psalms',
      label: 'תהילים יומי',
      location: `תהילים ${firstLetters}-${toLetters(lastChapter)}`,
      target: {
        bookQueries: ['תהילים', 'תהלים'],
        matchTitleExactly: true,
        tocPatterns: headingTocPatterns('פרק', firstLetters),
      },
    }
  })

  tryAdd(() => {
    const entries = new learning.MishnaYomiIndex().lookup(hebrewDate) as Array<{ k: string; v: string }>
    const first = entries[0]
    if (!first) return null
    const tractate = TRACTATE_HE[first.k] ?? first.k
    const [chapter, mishnah] = first.v.split(':').map((part) => parseInt(part, 10))
    if (!chapter) return null
    return {
      key: 'mishnah-yomit',
      label: 'משנה יומית',
      location: `${tractate} ${toLetters(chapter)}${mishnah ? `:${toLetters(mishnah)}` : ''}`,
      target: { bookQueries: [`משנה ${tractate}`], tocPatterns: headingTocPatterns('פרק', toLetters(chapter)) },
    }
  })

  tryAdd(() => {
    // perekYomi → { k: tractate (English), v: chapter } — the Mishnah-a-chapter cycle.
    const reading = learning.perekYomi(hebrewDate)
    const tractate = TRACTATE_HE[reading.k] ?? reading.k
    const chapterLetters = toLetters(reading.v)
    return {
      key: 'perek-yomi',
      label: 'פרק יומי',
      location: `${tractate} ${chapterLetters}`,
      target: { bookQueries: [`משנה ${tractate}`], tocPatterns: headingTocPatterns('פרק', chapterLetters) },
    }
  })

  tryAdd(() => {
    const dafText = cleanHebrew(new learning.DafYomi(hebrewDate).render('he'))
    const parsed = parseDafYomiString(dafText)
    if (!parsed) return null
    return {
      key: 'daf-yomi',
      label: 'דף יומי',
      location: dafText,
      target: {
        bookQueries: [`בבלי ${parsed.tractate}`],
        tocPatterns: [`${parsed.dafPrefix} %`, parsed.dafPrefix, `${parsed.dafPrefix}%`],
      },
    }
  })

  tryAdd(() => {
    const dafText = cleanHebrew(learning.dafWeekly(hebrewDate).render('he'))
    const parsed = parseDafYomiString(dafText)
    if (!parsed) return null
    return {
      key: 'daf-weekly',
      label: 'דף שבועי',
      location: dafText,
      target: {
        bookQueries: [`בבלי ${parsed.tractate}`],
        tocPatterns: [`${parsed.dafPrefix} %`, parsed.dafPrefix, `${parsed.dafPrefix}%`],
      },
    }
  })

  tryAdd(() => {
    // Rendered as "Dirshu Amud HaYomi: <tractate> דף <letters> ע<א|ב>".
    const rendered = cleanHebrew(new learning.DirshuAmudYomiEvent(hebrewDate).render('he'))
    const hebrewPart = rendered.slice(rendered.indexOf(':') + 1).trim()
    const match = /^(.+?) דף (\S+)(?: ע(א|ב))?$/.exec(hebrewPart)
    if (!match) return null
    const [, tractate, dafLetters, amudLetter] = match
    return {
      key: 'amud-yomi',
      label: 'עמוד יומי',
      location: hebrewPart,
      target: { bookQueries: [`בבלי ${tractate}`], tocPatterns: dafTocPatterns(dafLetters!, amudLetter) },
    }
  })

  tryAdd(() => {
    const reading = learning.yerushalmiYomi(hebrewDate, learning.vilna)
    if (!reading) return null
    const tractate = TRACTATE_HE[reading.name] ?? reading.name
    const dafLetters = toLetters(reading.blatt)
    return {
      key: 'yerushalmi-yomi',
      label: 'ירושלמי יומי',
      location: `${tractate} דף ${dafLetters}`,
      target: { bookQueries: [`ירושלמי ${tractate}`], tocPatterns: dafTocPatterns(dafLetters) },
    }
  })

  /** Mishneh Torah reading → card item; `name` is the Hebrew halachot title from hebcal's locale. */
  const rambamItem = (
    key: 'rambam-1' | 'rambam-3',
    label: string,
    readings: Array<{ name: string; perek: number | string }>,
  ): HomeDailyLearningItem | null => {
    const first = readings[0]
    if (!first) return null
    const toPerekText = (perek: number | string) => (typeof perek === 'number' ? toLetters(perek) : String(perek))
    const firstName = cleanHebrew(Locale.gettext(first.name, 'he'))
    const firstPerek = toPerekText(first.perek)
    return {
      key,
      label,
      location: readings
        .map((reading) => `${cleanHebrew(Locale.gettext(reading.name, 'he'))} ${toPerekText(reading.perek)}`)
        .join(', '),
      target: {
        bookQueries: [`רמבם ${firstName}`, `משנה תורה ${firstName}`, firstName],
        tocPatterns: headingTocPatterns('פרק', firstPerek),
      },
    }
  }

  tryAdd(() => rambamItem('rambam-1', 'רמב"ם פרק אחד', [learning.dailyRambam1(hebrewDate)]))
  tryAdd(() => rambamItem('rambam-3', 'רמב"ם שלושה פרקים', learning.dailyRambam3(hebrewDate)))

  tryAdd(() => {
    // chofetzChaim → { k: section, b: klal.seif, e: klal.seif } (numbers like 2.5).
    const reading = learning.chofetzChaim(hebrewDate) as { k: string; b: number | string; e: number | string }
    const section = CHOFETZ_CHAIM_SECTION_HEBREW[reading.k]
    const klal = Math.floor(Number(reading.b))
    const klalLetters = klal > 0 ? toLetters(klal) : null
    return {
      key: 'chofetz-chaim',
      label: 'חפץ חיים',
      location: section && klalLetters ? `${section} כלל ${klalLetters}` : `חפץ חיים ${reading.b}-${reading.e}`,
      target: {
        bookQueries: ['חפץ חיים'],
        tocPatterns: klalLetters
          ? [...(section ? [`%${section}%כלל ${klalLetters}%`] : []), ...headingTocPatterns('כלל', klalLetters)]
          : [],
      },
    }
  })

  tryAdd(() => {
    // shemiratHaLashon → { bk: part number, k: gate name (English), b: chapter.section, e }.
    const reading = learning.shemiratHaLashon(hebrewDate) as { bk: number; b: number | string }
    if (!reading) return null
    const chapter = Math.floor(Number(reading.b))
    const partLetters = toLetters(reading.bk)
    const chapterLetters = chapter > 0 ? toLetters(chapter) : null
    return {
      key: 'shemirat-halashon',
      label: 'שמירת הלשון',
      location: `חלק ${partLetters}${chapterLetters ? ` פרק ${chapterLetters}` : ''}`,
      target: {
        bookQueries: ['שמירת הלשון'],
        tocPatterns: chapterLetters
          ? [`%חלק ${partLetters}%פרק ${chapterLetters}%`, ...headingTocPatterns('פרק', chapterLetters)]
          : [],
      },
    }
  })

  tryAdd(() => {
    // kitzurShulchanAruch → { b: "siman:seif", e }; brief render is the Hebrew range.
    const reading = learning.kitzurShulchanAruch(hebrewDate)
    if (!reading) return null
    const siman = parseInt(String(reading.b).split(/[:.]/)[0] ?? '', 10)
    const simanLetters = siman > 0 ? toLetters(siman) : null
    return {
      key: 'kitzur-shulchan-aruch',
      label: 'קיצור שולחן ערוך',
      location: cleanHebrew(new learning.KitzurShulchanAruchEvent(hebrewDate, reading).renderBrief('he')),
      target: {
        bookQueries: ['קיצור שולחן ערוך'],
        tocPatterns: simanLetters ? headingTocPatterns('סימן', simanLetters) : [],
      },
    }
  })

  tryAdd(() => {
    // arukhHaShulchanYomi → { k: section (English), v: "siman.seif-siman.seif" }.
    const reading = learning.arukhHaShulchanYomi(hebrewDate)
    if (!reading) return null
    const section = cleanHebrew(Locale.gettext(reading.k, 'he'))
    const siman = parseInt(reading.v.split(/[.-]/)[0] ?? '', 10)
    const simanLetters = siman > 0 ? toLetters(siman) : null
    return {
      key: 'arukh-hashulchan-yomi',
      label: 'ערוך השולחן היומי',
      location: cleanHebrew(new learning.ArukhHaShulchanYomiEvent(hebrewDate, reading).render('he')),
      target: {
        bookQueries: [`ערוך השולחן ${section}`, 'ערוך השולחן'],
        tocPatterns: simanLetters ? headingTocPatterns('סימן', simanLetters) : [],
      },
    }
  })

  tryAdd(() => {
    // Summer Shabbatot only; null on every other day (diaspora schedule).
    const chapters = learning.pirkeiAvot(hebrewDate, false)
    if (!chapters?.length) return null
    const firstLetters = toLetters(chapters[0]!)
    return {
      key: 'pirkei-avot',
      label: 'פרקי אבות',
      location: `פרקי אבות ${chapters.map(toLetters).join('-')}`,
      target: { bookQueries: ['משנה אבות', 'פרקי אבות'], tocPatterns: headingTocPatterns('פרק', firstLetters) },
    }
  })

  return items
}
