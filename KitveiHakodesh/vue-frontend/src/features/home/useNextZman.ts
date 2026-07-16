import { ref, computed, onMounted } from 'vue'
import { useIntervalFn } from '@vueuse/core'
import { CITIES } from '@/features/hebrew-calendar/calendarTypes'
import type { CalendarZmanim, City } from '@/features/hebrew-calendar/calendarTypes'
import { lsGet, KEYS } from '@/utils/persistence'

/**
 * Chronological order + Hebrew labels for the zmanim we surface on the home bar.
 * Also drives the "all times" popup, so this is the full day list.
 */
export const ZMAN_ORDER: Array<{ key: keyof CalendarZmanim; label: string }> = [
  { key: 'alot', label: 'עלות השחר' },
  { key: 'misheyakir', label: 'משיכיר' },
  { key: 'sunrise', label: 'הנץ החמה' },
  { key: 'sofShmaGra', label: 'סו״ז ק״ש (גר״א)' },
  { key: 'sofShmaMga', label: 'סו״ז ק״ש (מג״א)' },
  { key: 'sofTfillaGra', label: 'סו״ז תפילה (גר״א)' },
  { key: 'sofTfillaMga', label: 'סו״ז תפילה (מג״א)' },
  { key: 'chatzot', label: 'חצות' },
  { key: 'minchaGedola', label: 'מנחה גדולה' },
  { key: 'minchaKetana', label: 'מנחה קטנה' },
  { key: 'plag', label: 'פלג המנחה' },
  { key: 'sunset', label: 'שקיעה' },
  { key: 'tzeit', label: 'צאת הכוכבים' },
]

/** The subset used to pick "the next zman" — short labels for the compact bar. */
const NEXT_LABELS: Partial<Record<keyof CalendarZmanim, string>> = {
  alot: 'עלות השחר',
  misheyakir: 'משיכיר',
  sunrise: 'הנץ החמה',
  sofShmaGra: 'סו״ז ק״ש',
  sofTfillaGra: 'סו״ז תפילה',
  chatzot: 'חצות',
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

export type ZmanUrgency = 'normal' | 'soon' | 'imminent'

export interface NextZman {
  key: keyof CalendarZmanim
  label: string
  time: Date
  /** Minutes until the zman (>= 0). */
  minutesUntil: number
  urgency: ZmanUrgency
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

    // Search today's ordered zmanim, then roll into tomorrow's once all passed.
    const day = cachedToday
    const nextDay = cachedTomorrow
    for (const src of [day, nextDay]) {
      for (const { key } of ZMAN_ORDER) {
        const t = src[key]
        const label = NEXT_LABELS[key]
        if (!t || !label) continue
        if (t.getTime() > current.getTime()) {
          const minutesUntil = Math.max(0, Math.round((t.getTime() - current.getTime()) / 60000))
          const urgency: ZmanUrgency =
            minutesUntil <= IMMINENT_MIN ? 'imminent' : minutesUntil <= SOON_MIN ? 'soon' : 'normal'
          return { key, label, time: t, minutesUntil, urgency }
        }
      }
    }
    return null
  })

  const displayTime = computed(() => (next.value ? fmt(next.value.time) : null))

  /** Full day table for the popup — every zman, marking the next + the passed. */
  const rows = computed<ZmanRow[]>(() => {
    if (!ready.value || !cachedToday) return []
    // Touch `now` so the table re-marks isNext/passed as time advances.
    const current = now.value
    const nextKey = next.value?.key
    return ZMAN_ORDER.map(({ key, label }) => {
      const t = cachedToday![key]
      return {
        key,
        label,
        time: t ? fmt(t) : '—',
        isNext: key === nextKey,
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
        alot: null, misheyakir: null, sunrise: null, sofShmaGra: null, sofShmaMga: null,
        sofTfillaGra: null, sofTfillaMga: null, chatzot: null, minchaGedola: null,
        minchaKetana: null, plag: null, sunset: null, tzeit: null,
      }
      try {
        const gloc = new GeoLocation(c.name, c.lat, c.lng, c.elevation, c.tzid)
        const z = new Zmanim(gloc, date, false)
        const norm = (d: Date | null) => (d && !isNaN(d.getTime()) ? d : null)
        return {
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

  return { next, displayTime, rows, city }
}
