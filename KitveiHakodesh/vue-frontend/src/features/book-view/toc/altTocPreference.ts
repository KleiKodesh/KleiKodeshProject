/**
 * Which alt structure is the preferred one: it leads the alt-TOC pane's list, and
 * scrolling the book follows it alone.
 */
import type { AltTocSection } from './useBookViewToc'

/**
 * Preference of an alt structure's key, lowest first. Larger divisions come before
 * fine counters (seifim, letters). Keys not listed sit between them and keep db order.
 */
function altTocStructurePriority(key: string): number {
  switch (key.toLowerCase()) {
    case 'daf':        return 0
    case 'parasha':    return 1
    case 'hilchot':    return 2
    case 'chapters':
    case 'chapter':    return 3
    case 'simannames':
    case 'simanim':
    case 'siman':      return 4
    case 'topic':      return 5
    case 'section':    return 6
    case 'seifim':     return 200
    case 'letter':     return 201
    default:           return 100
  }
}

/** The sections with the preferred structure first (stable sort keeps db order on ties). */
export function sortByPreference(sections: AltTocSection[]): AltTocSection[] {
  return [...sections].sort(
    (a, b) => altTocStructurePriority(a.structure.key) - altTocStructurePriority(b.structure.key),
  )
}
