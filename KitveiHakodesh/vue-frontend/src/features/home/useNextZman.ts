import { ref, computed, onMounted } from 'vue'
import { t } from '@/i18n'
import { useIntervalFn } from '@vueuse/core'
import { CITIES } from '@/features/hebrew-calendar/calendarTypes'
import type { CalendarZmanim, City } from '@/features/hebrew-calendar/calendarTypes'
import { lsGet } from '@/utils/persistence'
import { ZMANIM_CITY_KEY } from '@/features/hebrew-calendar/useZmanim'
import type { DayZmanim } from '@/features/hebrew-calendar/itimLabinaZmanim'

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
  { key: 'chatzotNight', label: t('home.zman.chatzotNight') },
  { key: 'alot', label: t('home.zman.alot') },
  { key: 'misheyakir', label: t('home.zman.misheyakir') },
  { key: 'sunrise', label: t('home.zman.sunrise') },
  // MGA's day starts at alot, so each MGA deadline lands BEFORE its GRA
  // counterpart — and tfilla-MGA still lands after shma-GRA (gap = one GRA
  // hour minus 24 min, positive at any habitable latitude).
  { key: 'sofShmaMga', label: t('home.zman.sofShmaMga') },
  { key: 'sofShmaGra', label: t('home.zman.sofShmaGra') },
  { key: 'sofTfillaMga', label: t('home.zman.sofTfillaMga') },
  { key: 'sofTfillaGra', label: t('home.zman.sofTfillaGra') },
  { key: 'chatzot', label: t('home.zman.chatzot') },
  { key: 'minchaGedola', label: t('home.zman.minchaGedola') },
  { key: 'minchaKetana', label: t('home.zman.minchaKetana') },
  { key: 'plag', label: t('home.zman.plag') },
  { key: 'sunset', label: t('home.zman.sunset') },
  { key: 'tzeit', label: t('home.zman.tzeit') },
]

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
  /** As the Itim Labina luach prints it. */
  printed: string
  /** Minutes until the zman (>= 0). */
  minutesUntil: number
  urgency: ZmanUrgency
  /** True only when imminent AND a deadline-critical zman — drives the pulse. */
  flash: boolean
}

export interface ZmanRow {
  key: keyof CalendarZmanim
  label: string
  time: string // as the Itim Labina luach prints it, or '—'
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

/**
 * Tracks the nearest upcoming zman for the home date bar and exposes the full
 * day's table for the "all times" popup.
 *
 * Load-time safety: the heavy Itim Labina zmanim engine (kosherZmanim) is
 * pulled in via a dynamic import fired in onMounted (mirroring homeDateInfo),
 * so it never lands in the home page's eager bundle and never blocks first
 * paint. Until it resolves, `next` and `rows` are empty and the bar renders
 * without the zman item.
 */
export function useNextZman() {
  const now = ref(new Date())
  const city = ref<City>(CITIES[0]!)

  // Lazily-loaded engine + per-day cached Date computations.
  let calc: ((city: City, date: Date) => DayZmanim) | null = null
  const ready = ref(false)

  let cachedDayKey = ''
  let cachedToday: DayZmanim | null = null
  let cachedTomorrow: DayZmanim | null = null

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
      for (const { key, label } of ZMAN_ORDER) {
        const zman = src[key]
        if (!zman) continue
        const t = zman.time
        if (t.getTime() > current.getTime()) {
          const minutesUntil = Math.max(0, Math.round((t.getTime() - current.getTime()) / 60000))
          const urgency: ZmanUrgency =
            minutesUntil <= IMMINENT_MIN ? 'imminent' : minutesUntil <= SOON_MIN ? 'soon' : 'normal'
          const flash = urgency === 'imminent' && CRITICAL_KEYS.has(key)
          return { key, label, time: t, printed: zman.printed, minutesUntil, urgency, flash }
        }
      }
    }
    return null
  })

  const displayTime = computed(() => next.value?.printed ?? null)

  /**
   * Today's צאת הכוכבים — exposed so the home date bar can roll the Hebrew date
   * over at nightfall (single source of truth; no second zmanim computation).
   */
  const tzeit = computed<Date | null>(() => {
    if (!ready.value) return null
    ensureDay(now.value)
    return cachedToday?.tzeit?.time ?? null
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
    const fromTomorrow = !!n && cachedToday![n.key]?.time.getTime() !== nextTime
    const day = fromTomorrow && cachedTomorrow ? cachedTomorrow : cachedToday!
    return ZMAN_ORDER.map(({ key, label }) => {
      const zman = day[key]
      const t = zman?.time
      return {
        key,
        label,
        time: zman ? zman.printed : '—',
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
    // Deferred load — keeps the zmanim engine out of the home page's critical bundle.
    const { itimLabinaDayZmanim } = await import('@/features/hebrew-calendar/itimLabinaZmanim')
    calc = (c, date): DayZmanim => {
      const empty = {
        chatzotNight: null,
        alot: null, misheyakir: null, sunrise: null, sofShmaGra: null, sofShmaMga: null,
        sofTfillaGra: null, sofTfillaMga: null, chatzot: null, minchaGedola: null,
        minchaKetana: null, plag: null, sunset: null, tzeit: null,
      }
      try {
        return itimLabinaDayZmanim(c, date)
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
