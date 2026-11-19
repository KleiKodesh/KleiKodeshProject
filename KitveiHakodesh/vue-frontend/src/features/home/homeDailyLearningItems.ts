import {
  bavliDafTarget,
  chofetzChaimTarget,
  mishnahPerekTarget,
  parseDafYomiString,
  parshaTarget,
  rambamTarget,
  shemiratHalashonGateName,
  shemiratHalashonTarget,
  simanTarget,
  tanachPerekTarget,
  tehillimMonthDayTarget,
  yerushalmiDafTarget,
} from './homeDailyLearningTargets'
import { normalizeHeadingText } from './homeDailyLearningBookMatch'
import { t } from '@/i18n'
import type { HomeDailyLearningItem } from './homeDailyLearningTypes'

/**
 * Builds today's daily-learning items from @hebcal/learning + the weekly parsha
 * from @hebcal/core. Each builder is independent: one that throws or has no
 * reading today just yields no card. Book lookup happens later, by name, in
 * homeDailyLearningResolver.ts.
 */

const cleanHebrew = normalizeHeadingText

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
      label: t('home.daily.parsha'),
      location: parshaNames.join('-'),
      target: parshaTarget(torahBookTitleForParshaNumber(parshaNumber), firstParshaName),
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
      label: t('home.daily.nach'),
      location: `${bookName} ${chapterLetters}`,
      target: tanachPerekTarget(bookName, chapterLetters),
    }
  })

  tryAdd(() => {
    // dailyPsalms → [firstChapter, lastChapter] by day of the Hebrew month (the 30-day cycle).
    const [firstChapter, lastChapter] = learning.dailyPsalms(hebrewDate) as [number, number]
    const firstLetters = toLetters(firstChapter)
    return {
      key: 'daily-psalms',
      label: t('home.daily.tehillim'),
      location: `תהילים ${firstLetters}-${toLetters(lastChapter)}`,
      target: tehillimMonthDayTarget(toLetters(hebrewDate.getDate()), firstLetters),
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
      label: t('home.daily.mishnaYomit'),
      location: `${tractate} ${toLetters(chapter)}${mishnah ? `:${toLetters(mishnah)}` : ''}`,
      target: mishnahPerekTarget(tractate, toLetters(chapter)),
    }
  })

  tryAdd(() => {
    // perekYomi → { k: tractate (English), v: chapter } — the Mishnah-a-chapter cycle.
    const reading = learning.perekYomi(hebrewDate)
    const tractate = TRACTATE_HE[reading.k] ?? reading.k
    const chapterLetters = toLetters(reading.v)
    return {
      key: 'perek-yomi',
      label: t('home.daily.perekYomi'),
      location: `${tractate} ${chapterLetters}`,
      target: mishnahPerekTarget(tractate, chapterLetters),
    }
  })

  tryAdd(() => {
    // Summer Shabbatot only; null on every other day (diaspora schedule).
    const chapters = learning.pirkeiAvot(hebrewDate, false)
    if (!chapters?.length) return null
    const firstLetters = toLetters(chapters[0]!)
    return {
      key: 'pirkei-avot',
      label: t('home.daily.avot'),
      location: `פרקי אבות ${chapters.map(toLetters).join('-')}`,
      target: (() => {
        const avot = mishnahPerekTarget('אבות', firstLetters)
        return { ...avot, books: [...avot.books, { title: 'פרקי אבות' }] }
      })(),
    }
  })

  tryAdd(() => {
    const dafText = cleanHebrew(new learning.DafYomi(hebrewDate).render('he'))
    const parsed = parseDafYomiString(dafText)
    if (!parsed) return null
    return {
      key: 'daf-yomi',
      label: t('home.daily.dafYomi'),
      location: dafText,
      target: bavliDafTarget(parsed.tractate, parsed.dafLetters),
    }
  })

  tryAdd(() => {
    const dafText = cleanHebrew(learning.dafWeekly(hebrewDate).render('he'))
    const parsed = parseDafYomiString(dafText)
    if (!parsed) return null
    return {
      key: 'daf-weekly',
      label: t('home.daily.dafWeekly'),
      location: dafText,
      target: bavliDafTarget(parsed.tractate, parsed.dafLetters),
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
      label: t('home.daily.amudYomi'),
      location: hebrewPart,
      target: bavliDafTarget(tractate!, dafLetters!, amudLetter as 'א' | 'ב' | undefined),
    }
  })

  tryAdd(() => {
    const reading = learning.yerushalmiYomi(hebrewDate, learning.vilna)
    if (!reading) return null
    const tractate = TRACTATE_HE[reading.name] ?? reading.name
    const dafLetters = toLetters(reading.blatt)
    return {
      key: 'yerushalmi-yomi',
      label: t('home.daily.yerushalmi'),
      location: `${tractate} דף ${dafLetters}`,
      target: yerushalmiDafTarget(tractate, dafLetters),
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
    // Some readings are ranges: chapters ("8-9" = from chapter 8), mitzvah counts ("1-83"), the
    // contents list ("1:1-4:8"). Open at the first number's chapter; a book with no such
    // chapter heading (the mitzvah lists) opens at its start.
    const firstPerekNumber = typeof first.perek === 'number' ? first.perek : parseInt(first.perek, 10)
    const firstPerekLetters = firstPerekNumber > 0 ? toLetters(firstPerekNumber) : null
    // A range without ":" may be a mitzvah count; used only when the book has no such chapter.
    const mitzvahLetters = typeof first.perek === 'string' && !first.perek.includes(':') ? firstPerekLetters : null
    return {
      key,
      label,
      // Consecutive readings of the same halachot collapse: "הלכות X יז יח" not "הלכות X יז, הלכות X יח".
      location: readings
        .reduce<Array<{ name: string; perakim: string[] }>>((groups, reading) => {
          const name = cleanHebrew(Locale.gettext(reading.name, 'he'))
          const perek = toPerekText(reading.perek)
          const last = groups[groups.length - 1]
          if (last?.name === name) {
            if (!last.perakim.includes(perek)) last.perakim.push(perek)
          } else groups.push({ name, perakim: [perek] })
          return groups
        }, [])
        .map((group) => `${group.name} ${group.perakim.join(' ')}`)
        .join(', '),
      target: rambamTarget(firstName, firstPerekLetters, mitzvahLetters),
    }
  }

  tryAdd(() => rambamItem('rambam-1', t('home.daily.rambam1'), [learning.dailyRambam1(hebrewDate)]))
  tryAdd(() => rambamItem('rambam-3', t('home.daily.rambam3'), learning.dailyRambam3(hebrewDate)))

  tryAdd(() => {
    // chofetzChaim → { k: section, b: klal.seif, e: klal.seif } (numbers like 2.5).
    const reading = learning.chofetzChaim(hebrewDate) as { k: string; b: number | string; e: number | string }
    const section = CHOFETZ_CHAIM_SECTION_HEBREW[reading.k]
    // b is "klal.seif" in the halachic parts (2.5, '2.10'), the illustration number in Tziyurim.
    const [klal = NaN, seif = NaN] = String(reading.b ?? '').split('.').map((part) => parseInt(part, 10))
    const klalLetters = klal > 0 ? toLetters(klal) : null
    const seifLetters = seif > 0 ? toLetters(seif) : null
    return {
      key: 'chofetz-chaim',
      label: t('home.daily.chofetzChaim'),
      location: section && klalLetters ? `${section} כלל ${klalLetters}` : `חפץ חיים ${reading.b}-${reading.e}`,
      target: chofetzChaimTarget(reading.k, klalLetters, seifLetters),
    }
  })

  tryAdd(() => {
    // shemiratHaLashon → { bk: part number, k: gate name (English), b: chapter.section, e }.
    const reading = learning.shemiratHaLashon(hebrewDate) as { bk: number; k: string; b: number | string }
    if (!reading) return null
    const chapter = Math.floor(Number(reading.b))
    const partLetters = toLetters(reading.bk)
    const chapterLetters = chapter > 0 ? toLetters(chapter) : null
    const gate = shemiratHalashonGateName(reading.k)
    return {
      key: 'shemirat-halashon',
      label: t('home.daily.shmirasHalashon'),
      location: [`חלק ${partLetters}`, gate, chapterLetters && gate !== 'הקדמה' ? `פרק ${chapterLetters}` : null]
        .filter(Boolean)
        .join(' '),
      target: shemiratHalashonTarget(reading.bk, reading.k, chapterLetters),
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
      label: t('home.daily.kitzur'),
      location: cleanHebrew(new learning.KitzurShulchanAruchEvent(hebrewDate, reading).renderBrief('he')),
      target: simanTarget({ title: 'קיצור שולחן ערוך' }, simanLetters),
    }
  })

  return items
}
