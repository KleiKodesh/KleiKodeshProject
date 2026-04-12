import { ref, computed, watch } from 'vue'
import { HDate, HebrewCalendar, flags, Location, Zmanim, GeoLocation } from '@hebcal/core'
import { getDailyLearning } from '@/utils/hebrewLearning'
import type { City } from './useZmanim'

// ── Hebrew helpers ────────────────────────────────────────────────────────────

const MONTH_NAMES: Record<number, string> = {
  1: 'ניסן',
  2: 'אייר',
  3: 'סיון',
  4: 'תמוז',
  5: 'אב',
  6: 'אלול',
  7: 'תשרי',
  8: 'חשון',
  9: 'כסלו',
  10: 'טבת',
  11: 'שבט',
  12: 'אדר',
  13: 'אדר ב׳',
}

function stripNiqqud(s: string): string {
  // Strip niqqud (U+0591–U+05C7) but preserve U+05BE (מקף — Hebrew hyphen used in double-parasha names)
  return s.replace(/[\u0591-\u05BD\u05BF-\u05C7]/g, '')
}

function fmt(d: Date | null): string | null {
  if (!d || isNaN(d.getTime())) return null
  return d.toLocaleTimeString('he-IL', { hour: '2-digit', minute: '2-digit', hour12: false })
}

// Hebrew day numeral only, no geresh/gershayim punctuation
function dayGem(hd: HDate): string {
  const raw = hd.renderGematriya().split(' ')[0] ?? String(hd.getDate())
  // Strip geresh (U+05F3 ׳) and gershayim (U+05F4 ״)
  return raw.replace(/[\u05F3\u05F4]/g, '')
}

// "ניסן תשפ״ו" — month + year
function monthYearHe(hd: HDate): string {
  const parts = hd.renderGematriya().split(' ')
  const year = parts[parts.length - 1] ?? ''
  return `${MONTH_NAMES[hd.getMonth()] ?? ''} ${year}`
}

// ── Types ─────────────────────────────────────────────────────────────────────

export interface WeekDay {
  date: Date
  dayOfWeek: number // 0=Sun … 6=Sat
  gregDay: number // 21
  gregMonthYear: string // "ספטמבר 2025"
  hebrewDayGem: string // "כ״ה"
  hebrewDayName: string // "ראשון"
  isToday: boolean
  isShabbat: boolean
  isFriday: boolean
  holidays: string[]
  parasha: string | null
  candleLighting: string | null
  havdalah: string | null
  omer: string | null
  chanukahCandles: string | null
  shabbatMevarchim: string | null
  molad: string | null
  yomKippurKatan: string | null
  fastStart: string | null
  fastEnd: string | null
  dafYomi: string | null
  mishnaYomi: string | null
  nachYomi: string | null
  sunrise: string | null
  sunset: string | null
  zmanim: {
    alot: string | null
    misheyakir: string | null
    sunrise: string | null
    sofShmaGra: string | null
    sofShmaMga: string | null
    sofTfillaGra: string | null
    sofTfillaMga: string | null
    chatzot: string | null
    minchaGedola: string | null
    minchaKetana: string | null
    plag: string | null
    sunset: string | null
    tzeit: string | null
  }
}

export interface CalendarWeek {
  // Header label: "אלול תשפ״ה–תשרי תשפ״ו" style
  hebrewRangeLabel: string
  gregRangeLabel: string
  days: WeekDay[]
}

// ── Location resolution ───────────────────────────────────────────────────────

const LOOKUP_NAMES: Record<string, string> = {
  ירושלים: 'Jerusalem',
  'תל אביב': 'Tel Aviv',
  חיפה: 'Haifa',
  'באר שבע': 'Beer Sheva',
  אילת: 'Eilat',
  טבריה: 'Tiberias',
  'ניו יורק': 'New York',
  לונדון: 'London',
  מונטריאול: 'Montreal',
}

function resolveLocation(city: City): InstanceType<typeof Location> {
  const englishName = LOOKUP_NAMES[city.name]
  const looked = englishName ? Location.lookup(englishName) : null
  if (looked) return looked
  const isIL = city.tzid === 'Asia/Jerusalem'
  return new Location(city.lat, city.lng, isIL, city.tzid, city.name, isIL ? 'IL' : undefined)
}

function makeGloc(loc: InstanceType<typeof Location>): InstanceType<typeof GeoLocation> {
  return new GeoLocation(
    loc.getName(),
    loc.getLatitude(),
    loc.getLongitude(),
    loc.getElevation(),
    loc.getTzid(),
  )
}

// ── Build one week ────────────────────────────────────────────────────────────

const HOLIDAY_FLAGS =
  flags.CHAG |
  flags.MINOR_FAST |
  flags.MAJOR_FAST |
  flags.ROSH_CHODESH |
  flags.SPECIAL_SHABBAT |
  flags.MODERN_HOLIDAY |
  flags.CHOL_HAMOED |
  flags.MINOR_HOLIDAY

const DAY_NAMES_HE = ['ראשון', 'שני', 'שלישי', 'רביעי', 'חמישי', 'שישי', 'שבת']

function sundayOf(date: Date): Date {
  const d = new Date(date)
  d.setDate(d.getDate() - d.getDay())
  d.setHours(0, 0, 0, 0)
  return d
}

function buildWeek(
  sunday: Date,
  location: InstanceType<typeof Location>,
  today0: Date,
): CalendarWeek {
  const saturday = new Date(sunday)
  saturday.setDate(sunday.getDate() + 6)

  const allEvents = HebrewCalendar.calendar({
    start: new HDate(sunday),
    end: new HDate(saturday),
    sedrot: true,
    candlelighting: true,
    havdalahMins: 50,
    location,
    il: location.getIsrael(),
    omer: true,
    shabbatMevarchim: true,
    molad: true,
    yomKippurKatan: true,
  })

  const byDate = new Map<string, typeof allEvents>()
  for (const e of allEvents) {
    const key = e.getDate().greg().toISOString().slice(0, 10)
    if (!byDate.has(key)) byDate.set(key, [])
    byDate.get(key)!.push(e)
  }

  const gloc = makeGloc(location)
  const days: WeekDay[] = []

  for (let d = 0; d < 7; d++) {
    const date = new Date(sunday)
    date.setDate(sunday.getDate() + d)
    date.setHours(0, 0, 0, 0)

    const hd = new HDate(date)
    const key = date.toISOString().slice(0, 10)
    const dayEvents = byDate.get(key) ?? []

    const holidays: string[] = []
    let parasha: string | null = null
    let candleLighting: string | null = null
    let havdalah: string | null = null
    let omer: string | null = null
    let chanukahCandles: string | null = null
    let shabbatMevarchim: string | null = null
    let molad: string | null = null
    let yomKippurKatan: string | null = null
    let fastStart: string | null = null
    let fastEnd: string | null = null

    for (const e of dayEvents) {
      const f = e.getFlags()
      if (f & flags.PARSHA_HASHAVUA) {
        parasha = stripNiqqud(e.render('he'))
      } else if (f & flags.LIGHT_CANDLES) {
        candleLighting = (e as any).eventTimeStr ?? null
      } else if (f & flags.YOM_TOV_ENDS || f & flags.LIGHT_CANDLES_TZEIS) {
        havdalah = (e as any).eventTimeStr ?? null
      } else if (f & flags.OMER_COUNT) {
        omer = stripNiqqud(e.render('he'))
      } else if (f & flags.CHANUKAH_CANDLES) {
        chanukahCandles = stripNiqqud(e.render('he'))
      } else if (f & flags.SHABBAT_MEVARCHIM) {
        shabbatMevarchim = stripNiqqud(e.render('he'))
      } else if (f & flags.MOLAD) {
        molad = stripNiqqud(e.render('he'))
      } else if (f & flags.YOM_KIPPUR_KATAN) {
        yomKippurKatan = stripNiqqud(e.render('he'))
      } else if (f & flags.MINOR_FAST || f & flags.MAJOR_FAST) {
        // Timed fast events have eventTimeStr (start/end times)
        const timeStr = (e as any).eventTimeStr
        if (timeStr) {
          const rendered = stripNiqqud(e.render('he'))
          if (rendered.includes('תחילת') || rendered.includes('עלות')) {
            fastStart = `${rendered}: ${timeStr}`
          } else {
            fastEnd = `${rendered}: ${timeStr}`
          }
        } else {
          holidays.push(stripNiqqud(e.render('he')))
        }
      } else if (f & HOLIDAY_FLAGS) {
        holidays.push(stripNiqqud(e.render('he')))
      }
    }

    // Daily learning
    const { dafYomi, mishnaYomi, nachYomi } = getDailyLearning(hd)

    let sunrise: string | null = null
    let sunset: string | null = null
    let zmanim = {
      alot: null as string | null,
      misheyakir: null as string | null,
      sunrise: null as string | null,
      sofShmaGra: null as string | null,
      sofShmaMga: null as string | null,
      sofTfillaGra: null as string | null,
      sofTfillaMga: null as string | null,
      chatzot: null as string | null,
      minchaGedola: null as string | null,
      minchaKetana: null as string | null,
      plag: null as string | null,
      sunset: null as string | null,
      tzeit: null as string | null,
    }
    try {
      const z = new Zmanim(gloc, date, false)
      sunrise = fmt(z.sunrise())
      sunset = fmt(z.sunset())
      zmanim = {
        alot: fmt(z.alotHaShachar()),
        misheyakir: fmt(z.misheyakir()),
        sunrise: fmt(z.sunrise()),
        sofShmaGra: fmt(z.sofZmanShma()),
        sofShmaMga: fmt(z.sofZmanShmaMGA()),
        sofTfillaGra: fmt(z.sofZmanTfilla()),
        sofTfillaMga: fmt(z.sofZmanTfillaMGA()),
        chatzot: fmt(z.chatzot()),
        minchaGedola: fmt(z.minchaGedola()),
        minchaKetana: fmt(z.minchaKetana()),
        plag: fmt(z.plagHaMincha()),
        sunset: fmt(z.sunset()),
        tzeit: fmt(z.tzeit()),
      }
    } catch {}

    days.push({
      date,
      dayOfWeek: d,
      gregDay: date.getDate(),
      gregMonthYear: date.toLocaleDateString('he-IL', { month: 'long', year: 'numeric' }),
      hebrewDayGem: dayGem(hd),
      hebrewDayName: DAY_NAMES_HE[d]!,
      isToday: date.getTime() === today0.getTime(),
      isShabbat: d === 6,
      isFriday: d === 5,
      holidays,
      parasha,
      candleLighting,
      havdalah,
      omer,
      chanukahCandles,
      shabbatMevarchim,
      molad,
      yomKippurKatan,
      fastStart,
      fastEnd,
      dafYomi,
      mishnaYomi,
      nachYomi,
      sunrise,
      sunset,
      zmanim,
    })
  }

  // Header labels
  const hdSun = new HDate(sunday)
  const hdSat = new HDate(saturday)
  const sunMonth = MONTH_NAMES[hdSun.getMonth()] ?? ''
  const satMonth = MONTH_NAMES[hdSat.getMonth()] ?? ''
  const sunYear = hdSun.renderGematriya().split(' ').pop() ?? ''
  const satYear = hdSat.renderGematriya().split(' ').pop() ?? ''

  const hebrewRangeLabel =
    sunMonth === satMonth
      ? `${sunMonth} ${sunYear}`
      : sunYear === satYear
        ? `${sunMonth} – ${satMonth} ${satYear}`
        : `${sunMonth} ${sunYear} – ${satMonth} ${satYear}`

  const gregSunMonth = sunday.toLocaleDateString('he-IL', { month: 'long' })
  const gregSatMonth = saturday.toLocaleDateString('he-IL', { month: 'long' })
  const gregSunYear = sunday.getFullYear().toString()
  const gregSatYear = saturday.getFullYear().toString()

  const gregRangeLabel =
    gregSunMonth === gregSatMonth
      ? `${gregSunMonth} ${gregSunYear}`
      : gregSunYear === gregSatYear
        ? `${gregSunMonth} – ${gregSatMonth} ${gregSatYear}`
        : `${gregSunMonth} ${gregSunYear} – ${gregSatMonth} ${gregSatYear}`

  return { hebrewRangeLabel, gregRangeLabel, days }
}

// ── Composable ────────────────────────────────────────────────────────────────

export function useWeeklyCalendar(city: { value: City }) {
  const today0 = new Date()
  today0.setHours(0, 0, 0, 0)

  // Current week offset from today's week (0 = this week, -1 = last week, etc.)
  const weekOffset = ref(0)

  const currentSunday = computed(() => {
    const s = sundayOf(today0)
    s.setDate(s.getDate() + weekOffset.value * 7)
    return s
  })

  const week = computed(() => {
    const loc = resolveLocation(city.value)
    return buildWeek(currentSunday.value, loc, today0)
  })

  function prevWeek() {
    weekOffset.value--
  }
  function nextWeek() {
    weekOffset.value++
  }
  function goToToday() {
    weekOffset.value = 0
  }

  const isCurrentWeek = computed(() => weekOffset.value === 0)

  // Reset offset when city changes
  watch(
    () => city.value,
    () => {
      weekOffset.value = 0
    },
  )

  return { week, weekOffset, isCurrentWeek, prevWeek, nextWeek, goToToday }
}
