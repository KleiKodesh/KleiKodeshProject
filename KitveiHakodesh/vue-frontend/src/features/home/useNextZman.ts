import { ref, computed, onMounted } from 'vue'
import { useIntervalFn } from '@vueuse/core'
import { CITIES } from '@/features/hebrew-calendar/calendarTypes'
import type { CalendarZmanim, City } from '@/features/hebrew-calendar/calendarTypes'
import { lsGet } from '@/utils/persistence'
import { ZMANIM_CITY_KEY } from '@/features/hebrew-calendar/useZmanim'

const KEYS = { SETTINGS_ZMANIM_CITY: ZMANIM_CITY_KEY } as const

/**
 * Chronological order + Hebrew labels for the zmanim we surface on the home bar.
 * Also drives the "all times" popup, so this is the full day list.
 */
export const ZMAN_ORDER: Array<{ key: keyof CalendarZmanim; label: string }> = [
  // chatzotNight of date D is the midpoint of the night ending at D's sunrise —
  // always before alot(D) and after tzeit(D-1), so it opens the civil day and
  // the ordered scan below keeps its ascending-times invariant. (In winter that
  // midpoint can fall before civil midnight, on date D-1.)
  { key: 'chatzotNight', label: 'חצות הלילה' },
  { key: 'alot', label: 'עלות השחר' },
  { key: 'misheyakir', label: 'משיכיר' },
  { key: 'sunrise', label: 'הנץ החמה' },
  // MGA's day starts at alot, so each MGA deadline lands BEFORE its GRA
  // counterpart — and tfilla-MGA still lands after shma-GRA (gap = one GRA
  // hour minus 24 min, positive at any habitable latitude).
  { key: 'sofShmaMga', label: 'סו״ז ק״ש (מג״א)' },
  { key: 'sofShmaGra', label: 'סו״ז ק״ש (גר״א)' },
  { key: 'sofTfillaMga', label: 'סו״ז תפילה (מג״א)' },
  { key: 'sofTfillaGra', label: 'סו״ז תפילה (גר״א)' },
  { key: 'chatzot', label: 'חצות היום' },
  { key: 'minchaGedola', label: 'מנחה גדולה' },
  { key: 'minchaKetana', label: 'מנחה קטנה' },
  { key: 'plag', label: 'פלג המנחה' },
  { key: 'sunset', label: 'שקיעה' },
  { key: 'tzeit', label: 'צאת הכוכבים' },
]

/** The subset used to pick "the next zman" — short labels for the compact bar. */
const NEXT_LABELS: Partial<Record<keyof CalendarZmanim, string>> = {
  chatzotNight: 'חצות הלילה',
  alot: 'עלות השחר',
  misheyakir: 'משיכיר',
  sunrise: 'הנץ החמה',
  sofShmaGra: 'סו״ז ק״ש',
  sofTfillaGra: 'סו״ז תפילה',
  chatzot: 'חצות היום',
  minchaGedola: 'מנחה גדולה',
  minchaKetana: 'מנחה קטנה',
  plag: 'פלג המנחה',
  sunset: 'שקיעה',
  tzeit: 'צאת הכוכבים',
}

/**
 * Minutes-remaining thresholds that drive the color cue. Kept tight so the
 * warning only kicks in when the zman is genuinely close — not tens of minutes
 * out.
 */
const SOON_MIN = 20
const IMMINENT_MIN = 8

/**
 * Zmanim with a halachic deadline the user actually races against. Only these
 * get the flashing "imminent" animation; the rest still color when close but
 * don't pulse.
 */
const CRITICAL_KEYS: ReadonlySet<keyof CalendarZmanim> = new Set([
  'alot',
  'sofShmaGra',
  'sofShmaMga',
  'sofTfillaGra',
  'sofTfillaMga',
  'sunset',
  'tzeit',
])

export type ZmanUrgency = 'normal' | 'soon' | 'imminent'

export interface NextZman {
  key: keyof CalendarZmanim
  label: string
  time: Date
  /** Minutes until the zman (>= 0). */
  minutesUntil: number
  urgency: ZmanUrgency
  /** True only when imminent AND a deadline-critical zman — drives the pulse. */
  flash: boolean
}

export interface ZmanRow {
  key: keyof CalendarZmanim
  label: string
  time: string // formatted HH:mm, or '—'
  isNext: boolean
  passed: boolean
}

function resolveCity(): City {
  const saved = lsGet<string>(KEYS.SETTINGS_ZMANIM_CITY)
  if (saved) {
    const found = CITIES.find((c) => c.name === saved)
    if (found) return found
  }
  return CITIES[0]! // Jerusalem
}

function fmt(d: Date): string {
  return d.toLocaleTimeString('he-IL', { hour: '2-digit', minute: '2-digit', hour12: false })
}

type ZmanDates = Record<keyof CalendarZmanim, Date | null>

/**
 * Tracks the nearest upcoming zman for the home date bar and exposes the full
 * day's table for the "all times" popup.
 *
 * Load-time safety: the heavy @hebcal/noaa + @hebcal/core zmanim engine is
 * pulled in via a dynamic import fired in onMounted (mirroring homeDateInfo),
 * so it never lands in the home page's eager bundle and never blocks first
 * paint. Until it resolves, `next` and `rows` are empty and the bar renders
 * without the zman item.
 */
export function useNextZman() {
  const now = ref(new Date())
  const city = ref<City>(CITIES[0]!)

  // Lazily-loaded engine + per-day cached Date computations.
  let calc: ((city: City, date: Date) => ZmanDates) | null = null
  const ready = ref(false)

  let cachedDayKey = ''
  let cachedToday: ZmanDates | null = null
  let cachedTomorrow: ZmanDates | null = null

  function ensureDay(reference: Date) {
    if (!calc) return
    const dayKey = reference.toDateString()
    if (dayKey === cachedDayKey && cachedToday) return
    cachedDayKey = dayKey
    const tomorrow = new Date(reference)
    tomorrow.setDate(tomorrow.getDate() + 1)
    cachedToday = calc(city.value, reference)
    cachedTomorrow = calc(city.value, tomorrow)
  }

  const next = computed<NextZman | null>(() => {
    if (!ready.value) return null
    const current = now.value
    ensureDay(current)
    if (!cachedToday || !cachedTomorrow) return null

    // Days are keyed by the actual civil date only — the post-tzeit Hebrew-date
    // advance (HomePageDateBar's dateReference) is display-only and never leaks
    // in here. Search today's ordered zmanim, then roll into tomorrow's once all
    // passed, so the bar always has an upcoming zman to show.
    for (const src of [cachedToday, cachedTomorrow]) {
      for (const { key } of ZMAN_ORDER) {
        const t = src[key]
        const label = NEXT_LABELS[key]
        if (!t || !label) continue
        if (t.getTime() > current.getTime()) {
          const minutesUntil = Math.max(0, Math.round((t.getTime() - current.getTime()) / 60000))
          const urgency: ZmanUrgency =
            minutesUntil <= IMMINENT_MIN ? 'imminent' : minutesUntil <= SOON_MIN ? 'soon' : 'normal'
          const flash = urgency === 'imminent' && CRITICAL_KEYS.has(key)
          return { key, label, time: t, minutesUntil, urgency, flash }
        }
      }
    }
    return null
  })

  const displayTime = computed(() => (next.value ? fmt(next.value.time) : null))

  /**
   * Today's צאת הכוכבים — exposed so the home date bar can roll the Hebrew date
   * over at nightfall (single source of truth; no second zmanim computation).
   */
  const tzeit = computed<Date | null>(() => {
    if (!ready.value) return null
    ensureDay(now.value)
    return cachedToday?.tzeit ?? null
  })

  /** Full day table for the popup — every zman, marking the next + the passed. */
  const rows = computed<ZmanRow[]>(() => {
    if (!ready.value || !cachedToday) return []
    // Touch `now` so the table re-marks isNext/passed as time advances.
    const current = now.value
    const n = next.value
    const nextTime = n?.time.getTime()
    // Show the day the next zman belongs to: today's table normally, tomorrow's
    // once all of today's zmanim have passed (post-tzeit the date display has
    // advanced too, so the popup stays consistent with the bar and the row of
    // the next zman is actually present to highlight).
    const fromTomorrow = !!n && cachedToday![n.key]?.getTime() !== nextTime
    const day = fromTomorrow && cachedTomorrow ? cachedTomorrow : cachedToday!
    return ZMAN_ORDER.map(({ key, label }) => {
      const t = day[key]
      return {
        key,
        label,
        time: t ? fmt(t) : '—',
        // Match by exact instant, not key, so a rolled-over next zman can never
        // highlight a same-named row from the wrong day.
        isNext: !!t && t.getTime() === nextTime,
        passed: !!t && t.getTime() <= current.getTime(),
      }
    })
  })

  onMounted(async () => {
    city.value = resolveCity()
    cachedDayKey = '' // force rebuild against the resolved city
    // Deferred load — keeps hebcal out of the home page's critical bundle.
    const [{ GeoLocation }, { Zmanim }] = await Promise.all([
      import('@hebcal/noaa'),
      import('@hebcal/core/dist/esm/zmanim'),
    ])
    calc = (c, date): ZmanDates => {
      const empty = {
        chatzotNight: null,
        alot: null, misheyakir: null, sunrise: null, sofShmaGra: null, sofShmaMga: null,
        sofTfillaGra: null, sofTfillaMga: null, chatzot: null, minchaGedola: null,
        minchaKetana: null, plag: null, sunset: null, tzeit: null,
      }
      try {
        const gloc = new GeoLocation(c.name, c.lat, c.lng, c.elevation, c.tzid)
        const z = new Zmanim(gloc, date, false)
        const norm = (d: Date | null) => (d && !isNaN(d.getTime()) ? d : null)
        return {
          chatzotNight: norm(z.chatzotNight()),
          alot: norm(z.alotHaShachar()),
          misheyakir: norm(z.misheyakir()),
          sunrise: norm(z.sunrise()),
          sofShmaGra: norm(z.sofZmanShma()),
          sofShmaMga: norm(z.sofZmanShmaMGA()),
          sofTfillaGra: norm(z.sofZmanTfilla()),
          sofTfillaMga: norm(z.sofZmanTfillaMGA()),
          chatzot: norm(z.chatzot()),
          minchaGedola: norm(z.minchaGedola()),
          minchaKetana: norm(z.minchaKetana()),
          plag: norm(z.plagHaMincha()),
          sunset: norm(z.sunset()),
          tzeit: norm(z.tzeit()),
        }
      } catch {
        return empty
      }
    }
    now.value = new Date()
    ready.value = true
  })

  useIntervalFn(() => {
    now.value = new Date()
  }, 30_000)

  return { next, displayTime, tzeit, rows, city, now }
}
