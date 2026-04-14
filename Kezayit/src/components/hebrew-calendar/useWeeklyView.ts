import { ref, computed, watch } from 'vue'
import { HDate, HebrewCalendar, flags, Location } from '@hebcal/core'
import { getDailyLearning } from '@/utils/hebrewLearning'
import { calcDayZmanim } from './useZmanim'
import type { City, CalendarDay, CalendarWeek } from './calendarTypes'

// ── Constants ─────────────────────────────────────────────────────────────────

const DAY_NAMES_HE = ['ראשון', 'שני', 'שלישי', 'רביעי', 'חמישי', 'שישי', 'שבת']

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

const HOLIDAY_FLAGS =
  flags.CHAG |
  flags.MINOR_FAST |
  flags.MAJOR_FAST |
  flags.ROSH_CHODESH |
  flags.SPECIAL_SHABBAT |
  flags.MODERN_HOLIDAY |
  flags.CHOL_HAMOED |
  flags.MINOR_HOLIDAY

const LOOKUP: Record<string, string> = {
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

// ── Helpers ───────────────────────────────────────────────────────────────────

function strip(s: string): string {
  return s.replace(/[\u0591-\u05BD\u05BF-\u05C7]/g, '')
}

function dayGem(hd: HDate): string {
  return (hd.renderGematriya().split(' ')[0] ?? String(hd.getDate())).replace(/[\u05F3\u05F4]/g, '')
}

function sundayOf(d: Date): Date {
  const s = new Date(d)
  s.setDate(s.getDate() - s.getDay())
  s.setHours(0, 0, 0, 0)
  return s
}

function resolveLocation(city: City): InstanceType<typeof Location> {
  const en = LOOKUP[city.name]
  const found = en ? Location.lookup(en) : null
  if (found) return found
  const il = city.tzid === 'Asia/Jerusalem'
  return new Location(city.lat, city.lng, il, city.tzid, city.name, il ? 'IL' : undefined)
}

function rangeLabel(aMonth: string, aYear: string, bMonth: string, bYear: string): string {
  if (aMonth === bMonth && aYear === bYear) return `${aMonth} ${aYear}`
  if (aYear === bYear) return `${aMonth} – ${bMonth} ${aYear}`
  return `${aMonth} ${aYear} – ${bMonth} ${bYear}`
}

// ── Build week ────────────────────────────────────────────────────────────────

function buildWeek(sunday: Date, city: City, today0: Date): CalendarWeek {
  const saturday = new Date(sunday)
  saturday.setDate(sunday.getDate() + 6)

  const loc = resolveLocation(city)
  const events = HebrewCalendar.calendar({
    start: new HDate(sunday),
    end: new HDate(saturday),
    sedrot: true,
    candlelighting: true,
    havdalahMins: 50,
    location: loc,
    il: loc.getIsrael(),
    omer: true,
    shabbatMevarchim: true,
    molad: true,
    yomKippurKatan: true,
  })

  const byDate = new Map<string, typeof events>()
  for (const e of events) {
    const k = e.getDate().greg().toISOString().slice(0, 10)
    if (!byDate.has(k)) byDate.set(k, [])
    byDate.get(k)!.push(e)
  }

  const days: CalendarDay[] = []

  for (let d = 0; d < 7; d++) {
    const date = new Date(sunday)
    date.setDate(sunday.getDate() + d)
    date.setHours(0, 0, 0, 0)
    const hd = new HDate(date)
    const dayEvents = byDate.get(date.toISOString().slice(0, 10)) ?? []

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
        parasha = strip(e.render('he')).replace(/^פרשת\s+/, '')
      } else if (f & flags.LIGHT_CANDLES) {
        candleLighting = (e as any).eventTimeStr ?? null
      } else if (f & flags.YOM_TOV_ENDS || f & flags.LIGHT_CANDLES_TZEIS) {
        havdalah = (e as any).eventTimeStr ?? null
      } else if (f & flags.OMER_COUNT) {
        omer = strip(e.render('he'))
      } else if (f & flags.CHANUKAH_CANDLES) {
        chanukahCandles = strip(e.render('he'))
      } else if (f & flags.SHABBAT_MEVARCHIM) {
        shabbatMevarchim = strip(e.render('he'))
      } else if (f & flags.MOLAD) {
        molad = strip(e.render('he'))
      } else if (f & flags.YOM_KIPPUR_KATAN) {
        yomKippurKatan = strip(e.render('he'))
      } else if (f & flags.MINOR_FAST || f & flags.MAJOR_FAST) {
        const timeStr = (e as any).eventTimeStr
        if (timeStr) {
          const r = strip(e.render('he'))
          if (r.includes('תחילת') || r.includes('עלות')) fastStart = `${r}: ${timeStr}`
          else fastEnd = `${r}: ${timeStr}`
        } else {
          holidays.push(strip(e.render('he')))
        }
      } else if (f & HOLIDAY_FLAGS) {
        holidays.push(strip(e.render('he')))
      }
    }

    days.push({
      date,
      dayOfWeek: d,
      gregDay: date.getDate(),
      hebGem: dayGem(hd),
      hebDayName: DAY_NAMES_HE[d]!,
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
      learning: getDailyLearning(hd),
      zmanim: calcDayZmanim(city, date),
    })
  }

  // Labels
  const hdSun = new HDate(sunday)
  const hdSat = new HDate(saturday)
  const sunYear = hdSun.renderGematriya().split(' ').pop() ?? ''
  const satYear = hdSat.renderGematriya().split(' ').pop() ?? ''

  const hebrewLabel = rangeLabel(
    MONTH_NAMES[hdSun.getMonth()] ?? '',
    sunYear,
    MONTH_NAMES[hdSat.getMonth()] ?? '',
    satYear,
  )
  const gregLabel = rangeLabel(
    sunday.toLocaleDateString('he-IL', { month: 'long' }),
    String(sunday.getFullYear()),
    saturday.toLocaleDateString('he-IL', { month: 'long' }),
    String(saturday.getFullYear()),
  )

  return { hebrewLabel, gregLabel, days }
}

// ── Composable ────────────────────────────────────────────────────────────────

export function useWeeklyView(city: { value: City }) {
  const today0 = new Date()
  today0.setHours(0, 0, 0, 0)

  const offset = ref(0)

  const currentSunday = computed(() => {
    const s = sundayOf(today0)
    s.setDate(s.getDate() + offset.value * 7)
    return s
  })

  const week = computed<CalendarWeek>(() => buildWeek(currentSunday.value, city.value, today0))

  watch(
    () => city.value,
    () => {
      offset.value = 0
    },
  )

  return {
    week,
    prev: () => {
      offset.value--
    },
    next: () => {
      offset.value++
    },
    goToday: () => {
      offset.value = 0
    },
    reset: () => {
      offset.value = 0
    },
  }
}
