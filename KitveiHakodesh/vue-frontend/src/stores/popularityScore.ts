/**
 * Popularity scoring shared by the home page's two dynamic tile groups.
 *
 * Both answer the same question — which of these did the user really mean to
 * keep? — so both answer it the same way: time-decayed frequency (LFU with
 * aging). A visit adds a point, and accumulated points halve every HALF_LIFE_MS.
 * Frequency is what ranks; age is what lets something the user has stopped
 * reaching for fall away without anyone deciding it should.
 *
 * Recency alone would rank a thing opened once an hour ago above the thing
 * opened every day for a month, and a single detour would evict a habit.
 *
 * Nothing here sweeps. Each entry records the instant its score was last worked
 * out, and the score is aged forward when next read or bumped — so an app left
 * closed for a month costs nothing on open and still comes back correctly aged.
 */

/** The fields an entry must carry to be ranked. */
export interface PopularityScored {
  /**
   * Decayed points as of `scoredAt`. Not comparable across entries until both
   * are decayed to a common instant — always rank via `decayedScore`.
   */
  score: number
  /** When `score` was last brought up to date (ms). */
  scoredAt: number
  /**
   * Most recent visit (ms), for tie-breaking. Named for what it is rather than
   * for either store's own field, so a store that already persists this under
   * another name maps to it rather than renaming a stored field.
   */
  lastVisitedAt: number
  /** Pinned entries sort ahead of the rest and survive the score floor. */
  pinned?: boolean
}

/** Points added per visit, before any decay. */
export const VISIT_POINTS = 1

/** Accumulated points lose half their value over this span. */
export const HALF_LIFE_MS = 14 * 24 * 60 * 60 * 1000

/**
 * Entries below this decayed score are dropped as noise — something opened once
 * and never again decays past it in roughly six half-lives. Without a floor the
 * list fills with one-off detours that the cap can never evict, because they
 * keep being re-added.
 */
export const MIN_SCORE = 0.02

/** The entry's score decayed forward to `now`. */
export function decayedScore(entry: PopularityScored, now: number): number {
  const elapsed = now - entry.scoredAt
  // Clock skew (or a system clock moved back) must not inflate a score.
  if (elapsed <= 0) return entry.score
  return entry.score * Math.pow(0.5, elapsed / HALF_LIFE_MS)
}

/** The entry with its score decayed to `now`, ready to be stored. */
export function decayEntry<T extends PopularityScored>(entry: T, now: number): T {
  return { ...entry, score: decayedScore(entry, now), scoredAt: now }
}

/** The score an entry should carry after a fresh visit at `now`. */
export function scoreAfterVisit(entry: PopularityScored, now: number): number {
  return decayedScore(entry, now) + VISIT_POINTS
}

/**
 * Ranks by decayed score, most popular first. Pins come first regardless of
 * score — a pin means the user asked for it, not that it earned the slot. Ties
 * fall back to recency so equal-score entries order predictably.
 */
export function sortByPopularity<T extends PopularityScored>(list: T[], now: number): T[] {
  return [...list].sort((a, b) => {
    const pinDelta = (b.pinned ? 1 : 0) - (a.pinned ? 1 : 0)
    if (pinDelta !== 0) return pinDelta
    const scoreDelta = decayedScore(b, now) - decayedScore(a, now)
    if (scoreDelta !== 0) return scoreDelta
    return b.lastVisitedAt - a.lastVisitedAt
  })
}

/**
 * Drops faded and surplus entries. Pinned entries survive the score floor, and
 * the remaining slots go to the highest-scoring unpinned entries.
 *
 * Two ways a list can freeze against newcomers, both closed here:
 *
 * `maxPinned` stops pins filling every slot — otherwise a fully pinned list
 * leaves no room for a new entry to accumulate a score, so it could never earn
 * its way in however often it was opened.
 *
 * `protectedKey` is the entry just visited, which keeps its slot regardless of
 * score. A newcomer enters at one point and would otherwise be evicted at once
 * by long-established entries, so it could never accumulate — and that newcomer
 * is exactly what the user is working on today.
 *
 * `floorBelowCap` decides whether a quiet entry may be dropped before the list
 * is even full — see the option's own note.
 */
export function capByPopularity<T extends PopularityScored>(
  list: T[],
  now: number,
  options: {
    max: number
    maxPinned: number
    keyOf: (entry: T) => string
    protectedKey?: string
    /**
     * Whether a faded entry may be dropped while the list is still under `max`.
     *
     * For something incidental — a folder, recreated the moment a file is opened
     * from it again — dropping it early keeps the list honest. For a list the
     * user curates and can pin, an entry that fell quiet is not the same as one
     * the user discarded, and there is no way back once it is gone: those keep
     * their place until the cap actually needs the slot.
     */
    floorBelowCap?: boolean
  },
): T[] {
  const { max, maxPinned, keyOf, protectedKey, floorBelowCap = true } = options
  const pinned = list.filter((e) => e.pinned).slice(0, maxPinned)
  const room = Math.max(0, max - pinned.length)
  const applyFloor = floorBelowCap || list.length > max
  const unpinned = list
    .filter(
      (e) =>
        !e.pinned &&
        (!applyFloor || keyOf(e) === protectedKey || decayedScore(e, now) >= MIN_SCORE),
    )
    .sort((a, b) => {
      // The protected entry sorts first so the slice below can never drop it.
      if (keyOf(a) === protectedKey) return -1
      if (keyOf(b) === protectedKey) return 1
      return decayedScore(b, now) - decayedScore(a, now)
    })
    .slice(0, room)
  return [...pinned, ...unpinned]
}

/**
 * Whether a pin should be refused because the pin budget is full. Silently
 * accepting it would let the cap drop the pin again on the next write, which
 * reads as the pin not having stuck.
 */
export function pinBudgetExhausted(list: PopularityScored[], maxPinned: number): boolean {
  return list.filter((e) => e.pinned).length >= maxPinned
}
