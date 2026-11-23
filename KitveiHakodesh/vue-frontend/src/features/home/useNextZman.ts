import { ref, shallowRef, computed, onMounted } from 'vue'
import { t } from '@/i18n'
import { useIntervalFn } from '@vueuse/core'
import { ZMAN_NAMES } from 'kleikodesh-zmanim'
import { ELEVATED_ZMANIM, zmanLabelKey, type CalendarZmanim } from '@/features/hebrew-calendar/calendarTypes'
import { lsSet } from '@/utils/persistence'
import { savedPlaceId, ZMANIM_CITY_KEY } from '@/features/hebrew-calendar/useZmanim'
import type { DayTimedZmanim, Place } from '@/features/hebrew-calendar/zmanimPlaces'

const KEYS = { SETTINGS_ZMANIM_CITY: ZMANIM_CITY_KEY } as const

/**
 * Every library zman with its label, in the library's order (chronological by opinion group,
 * each variant beside its siblings). Drives the bar and the "all times" popup.
 * chatzotNight of date D is the midpoint of the night ending at D's sunrise, so it opens the day.
 */
export const ZMAN_ORDER: Array<{ key: keyof CalendarZmanim; label: string }> =
  ZMAN_NAMES.map((key) => ({ key, label: t(zmanLabelKey(key)) }))

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
  /** As the calendar prints it. */
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
  time: string // as the calendar prints it, or '—'
  /** The sun never reaches this zman's angle today. */
  missing: boolean
  isNext: boolean
  passed: boolean
}

/**
 * Tracks the nearest upcoming zman for the home date bar and exposes the full
 * day's table for the "all times" popup.
 *
 * Load-time safety: the zmanim engine (kleikodesh-zmanim) is
 * pulled in via a dynamic import fired in onMounted (mirroring homeDateInfo),
 * so it never lands in the home page's eager bundle and never blocks first
 * paint. Until it resolves, `next` and `rows` are empty and the bar renders
 * without the zman item.
 */
export function useNextZman() {
  const now = ref(new Date())
  /** The place the times are for; null until loaded. */
  const city = shallowRef<Place | null>(null)

  // Lazily-loaded engine + per-day cached Date computations.
  let calc: ((place: Place, date: Date) => DayTimedZmanim) | null = null
  const ready = ref(false)

  let cachedDayKey = ''
  let cachedToday: DayTimedZmanim | null = null
  let cachedTomorrow: DayTimedZmanim | null = null

  function ensureDay(reference: Date) {
    if (!calc || !city.value) return
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
    // The list keeps each variant beside its counterpart, so it is not strictly
    // chronological: take the earliest upcoming zman of the day, not the first listed.
    for (const src of [cachedToday, cachedTomorrow]) {
      let best: { key: keyof CalendarZmanim; label: string; zman: NonNullable<DayTimedZmanim[keyof DayTimedZmanim]> } | null = null
      for (const { key, label } of ZMAN_ORDER) {
        const zman = src[key]
        if (!zman || zman.time.getTime() <= current.getTime()) continue
        if (!best || zman.time.getTime() < best.zman.time.getTime()) best = { key, label, zman }
      }
      if (best) {
        const { key, label, zman } = best
        const t = zman.time
        {
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
    if (!ready.value) return []
    // Touch `now` so the table re-marks isNext/passed as time advances, and
    // `next` (which fills the day cache) BEFORE checking the cache — otherwise,
    // with no other reader of `next`, the first run returns [] forever.
    const current = now.value
    const n = next.value
    if (!cachedToday) return []
    const nextTime = n?.time.getTime()
    // Show the day the next zman belongs to: today's table normally, tomorrow's
    // once all of today's zmanim have passed (post-tzeit the date display has
    // advanced too, so the popup stays consistent with the bar and the row of
    // the next zman is actually present to highlight).
    const fromTomorrow = !!n && cachedToday![n.key]?.time.getTime() !== nextTime
    const day = fromTomorrow && cachedTomorrow ? cachedTomorrow : cachedToday!
    return ZMAN_ORDER.filter(({ key }) => !(ELEVATED_ZMANIM.has(key) && day[key] == null)).map(({ key, label }) => {
      const zman = day[key]
      const t = zman?.time
      return {
        key,
        label,
        time: zman ? zman.printed : '—',
        missing: !zman,
        // Match by exact instant, not key, so a rolled-over next zman can never
        // highlight a same-named row from the wrong day.
        isNext: !!t && t.getTime() === nextTime,
        passed: !!t && t.getTime() <= current.getTime(),
      }
    })
  })

  onMounted(async () => {
    // Deferred load — keeps the zmanim engine out of the home page's critical bundle.
    const places = await import('@/features/hebrew-calendar/zmanimPlaces')
    city.value = await places.loadPlace(savedPlaceId())
    cachedDayKey = '' // force rebuild against the resolved place
    calc = places.dayTimedZmanim
    now.value = new Date()
    ready.value = true
  })

  useIntervalFn(() => {
    now.value = new Date()
  }, 30_000)

  /** Switch place: saved like the settings page choice, zmanim recomputed now. */
  async function setCity(id: number) {
    lsSet(KEYS.SETTINGS_ZMANIM_CITY, id)
    const { loadPlace } = await import('@/features/hebrew-calendar/zmanimPlaces')
    city.value = await loadPlace(id)
    cachedDayKey = ''
    now.value = new Date()
  }

  return { next, displayTime, tzeit, rows, city, now, setCity }
}
