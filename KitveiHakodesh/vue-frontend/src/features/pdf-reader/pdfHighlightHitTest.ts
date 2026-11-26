/**
 * Pure geometry for "Delete highlight": which highlights a point or a selection touches.
 * Everything is in PDF page space (points, y up), as PDF.js keeps a highlight's quadPoints / rect.
 */

/** An axis-aligned box: [x1, y1, x2, y2], any corner order. */
export type Box = readonly [number, number, number, number]

const norm = (b: Box): Box => [Math.min(b[0], b[2]), Math.min(b[1], b[3]), Math.max(b[0], b[2]), Math.max(b[1], b[3])]

/**
 * A highlight's boxes: one per quad of its quadPoints (8 numbers each, as PDF.js stores them),
 * else its rect (a free-hand highlight has no quads).
 */
export function highlightBoxes(quadPoints: ArrayLike<number> | null | undefined, rect: ArrayLike<number> | null | undefined): Box[] {
  const out: Box[] = []
  if (quadPoints && quadPoints.length >= 8) {
    for (let i = 0; i + 8 <= quadPoints.length; i += 8) {
      const xs = [quadPoints[i]!, quadPoints[i + 2]!, quadPoints[i + 4]!, quadPoints[i + 6]!]
      const ys = [quadPoints[i + 1]!, quadPoints[i + 3]!, quadPoints[i + 5]!, quadPoints[i + 7]!]
      out.push([Math.min(...xs), Math.min(...ys), Math.max(...xs), Math.max(...ys)])
    }
  } else if (rect && rect.length >= 4) out.push(norm([rect[0]!, rect[1]!, rect[2]!, rect[3]!]))
  return out
}

/** Do the two boxes share any area or edge (a point is a zero-size box)? */
export function boxesOverlap(a: Box, b: Box): boolean {
  const [ax1, ay1, ax2, ay2] = norm(a)
  const [bx1, by1, bx2, by2] = norm(b)
  return ax1 <= bx2 && bx1 <= ax2 && ay1 <= by2 && by1 <= ay2
}

/** Does any of the highlight's boxes touch any of the probe boxes? */
export function highlightTouches(boxes: readonly Box[], probes: readonly Box[]): boolean {
  return boxes.some((b) => probes.some((p) => boxesOverlap(b, p)))
}
