/**
 * Alt structures merged into the main TOC by the service (the seifim: see
 * SeforimDbQueries.GetAllTocEntries), rebuilt as label sections so their markers keep
 * showing in the text - each merged entry with its main-TOC parent, as the structure
 * held them.
 */
import type { TocEntry } from '@/webview-host/queries.types'
import type { AltTocSection } from './useBookViewToc'

export function mergedAltTocSections(entries: TocEntry[]): AltTocSection[] {
  const byStructure = new Map<number, TocEntry[]>()
  const parentIds = new Map<number, Set<number>>()
  for (const e of entries) {
    if (e.altStructureId == null) continue
    let list = byStructure.get(e.altStructureId)
    if (list == null) {
      byStructure.set(e.altStructureId, (list = []))
      parentIds.set(e.altStructureId, new Set())
    }
    list.push(e)
    if (e.parentId != null) parentIds.get(e.altStructureId)!.add(e.parentId)
  }
  return [...byStructure].map(([id, merged]) => {
    const parents = parentIds.get(id)!
    return {
      structure: { id, key: 'Seifim', title: null, heTitle: null },
      entries: [...entries.filter((e) => parents.has(e.id)), ...merged],
      searchTree: null,
    }
  })
}
