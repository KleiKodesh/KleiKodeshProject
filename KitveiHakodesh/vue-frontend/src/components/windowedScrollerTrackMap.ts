/**
 * The scrollbar track's mapping between list positions and thumb offsets, for
 * WindowedScrollerBar.vue.
 *
 * Plain proportion gives a section of a long book a few pixels of track, too thin
 * to point at. So the track is piecewise linear instead: landmark positions (the
 * starts of the list's sections, by the caller's reckoning — TOC entries) become
 * knots, and every span between knots is at least `minSpanPx` long, the rest of the
 * track being shared in proportion. Landmarks come in tiers, coarse first; a tier is
 * added only while every span can still be given its minimum — on a list with
 * hundreds of sections only the top level or two fit, and below that the track is
 * proportional as before. With no landmarks the map is the plain proportion.
 */

export interface TrackMap {
  /** Knot positions in list pixels, ascending, from 0 to the maximum position. */
  positions: number[]
  /** The thumb offset of each knot, ascending, from 0 to the travel. */
  offsets: number[]
}

export function buildTrackMap(
  landmarkTiers: readonly (readonly number[])[],
  maxPosition: number,
  travel: number,
  minSpanPx: number,
): TrackMap {
  if (!(maxPosition > 0) || !(travel > 0)) return { positions: [0, Math.max(0, maxPosition)], offsets: [0, Math.max(0, travel)] }

  const knots = new Set<number>([0, maxPosition])
  for (const tier of landmarkTiers) {
    const candidate = new Set(knots)
    for (const position of tier) {
      if (position > 0 && position < maxPosition) candidate.add(position)
    }
    if ((candidate.size - 1) * minSpanPx > travel) break
    for (const position of candidate) knots.add(position)
  }

  const positions = [...knots].sort((first, second) => first - second)
  const lengths = positions.slice(1).map((position, index) => position - positions[index]!)
  const spans = allocateSpans(lengths, travel, minSpanPx)

  const offsets = [0]
  for (const span of spans) offsets.push(offsets[offsets.length - 1]! + span)
  // Rounding drift goes to the last knot so the map ends exactly at the travel.
  offsets[offsets.length - 1] = travel
  return { positions, offsets }
}

/**
 * Gives every span its proportional share of `travel`, lifting any span below the
 * floor to the floor and re-sharing the remainder among the rest until none is
 * below it. The caller guarantees the floors alone fit within the travel.
 */
function allocateSpans(lengths: number[], travel: number, floor: number): number[] {
  const spans = new Array<number>(lengths.length).fill(0)
  const atFloor = new Array<boolean>(lengths.length).fill(false)
  for (;;) {
    let floored = 0
    let freeLength = 0
    for (let index = 0; index < lengths.length; index++) {
      if (atFloor[index]) floored++
      else freeLength += lengths[index]!
    }
    const remaining = travel - floored * floor
    let lifted = false
    for (let index = 0; index < lengths.length; index++) {
      if (atFloor[index]) {
        spans[index] = floor
        continue
      }
      const share = freeLength > 0 ? (remaining * lengths[index]!) / freeLength : remaining / (lengths.length - floored)
      if (share < floor) {
        atFloor[index] = true
        lifted = true
      } else {
        spans[index] = share
      }
    }
    if (!lifted) return spans
  }
}

/** The thumb offset for a list position. */
export function trackOffsetForPosition(map: TrackMap, position: number): number {
  return interpolate(map.positions, map.offsets, position)
}

/** The list position for a thumb offset. */
export function positionForTrackOffset(map: TrackMap, offset: number): number {
  return interpolate(map.offsets, map.positions, offset)
}

function interpolate(from: number[], to: number[], value: number): number {
  const last = from.length - 1
  if (last < 1) return to[0] ?? 0
  if (value <= from[0]!) return to[0]!
  if (value >= from[last]!) return to[last]!
  // The last knot at or below the value.
  let low = 0
  let high = last
  while (low < high) {
    const middle = (low + high + 1) >> 1
    if (from[middle]! <= value) low = middle
    else high = middle - 1
  }
  const span = from[low + 1]! - from[low]!
  const fraction = span > 0 ? (value - from[low]!) / span : 0
  return to[low]! + fraction * (to[low + 1]! - to[low]!)
}
