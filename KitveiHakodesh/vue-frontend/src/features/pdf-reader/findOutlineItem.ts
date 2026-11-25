/** One outline item as PDFDocumentProxy.getOutline() returns it (only what the match reads). */
export interface OutlineItemLike {
  title: string
  dest: string | unknown[] | null
  items: OutlineItemLike[]
}

const normalize = (text: string | undefined) =>
  (text || '').replace(/[֑-ׇ]/g, '').replace(/[^\p{L}\p{N}\s]/gu, ' ').replace(/\s+/g, ' ').trim()

/**
 * Finds the outline item for a middle-dot-separated section path (a book view breadcrumb).
 * Deepest segment first, then shallower ones; among items whose title matches a segment, the
 * one with the most matching ancestors wins. Exact normalised equality beats containment.
 * Returns null when nothing matches. (Same rule the old PDF.js viewer used for its outline jump.)
 */
export function findOutlineItem<T extends OutlineItemLike>(outline: readonly T[], path: string): T | null {
  const segments = path.split('·').map(normalize).filter(Boolean)
  const flat: { item: T; title: string; ancestors: string[] }[] = []
  const walk = (items: readonly T[], ancestors: string[]) => {
    for (const item of items) {
      const title = normalize(item.title)
      flat.push({ item, title, ancestors })
      walk((item.items ?? []) as T[], [...ancestors, title])
    }
  }
  walk(outline, [])
  const matches = (title: string, segment: string, exact: boolean) =>
    exact ? title === segment : title.includes(segment) || (title.length > 0 && segment.includes(title))
  for (const exact of [true, false]) {
    for (let i = segments.length - 1; i >= 0; i--) {
      let best: T | null = null
      let bestScore = -1
      for (const entry of flat) {
        if (!entry.item.dest || !matches(entry.title, segments[i]!, exact)) continue
        const score = segments.slice(0, i).filter((s) => entry.ancestors.some((a) => matches(a, s, exact))).length
        if (score > bestScore) {
          best = entry.item
          bestScore = score
        }
      }
      if (best) return best
    }
  }
  return null
}
