/**
 * Parses the active tab's tocPath string into structured segments, each with
 * its matching entry and the sibling list needed for the chevron dropdowns.
 *
 * Handles two route types:
 * - /book-view: reads TocEntry tree from the TocBridge registered in bookViewStore
 * - /pdf-view:  reads PdfOutlineEntry list from the PdfBridge registered in bookViewStore
 *
 * Only produces non-empty segments when the bridge is registered.
 */
import { computed } from 'vue'
import type { ComputedRef } from 'vue'
import type { TocEntry } from '@/webview-host/queries.types'
import type { TocBridge, PdfBridge, PdfOutlineEntry } from '@/stores/bookViewStore'

export interface TocBreadcrumbSegment {
  label: string
  tocEntry: TocEntry | null
  /** Siblings = children of this segment's parent (or root entries if depth 0). */
  siblings: TocEntry[]
  /** Children of this segment's own entry — for the trailing chevron on the active segment. */
  children: TocEntry[]
  isActive: boolean
}

export interface PdfBreadcrumbSegment {
  label: string
  outlineEntry: PdfOutlineEntry | null
  siblings: PdfOutlineEntry[]
  /** Children of this segment's own entry — for the trailing chevron on the active segment. */
  children: PdfOutlineEntry[]
  isActive: boolean
}

export type BreadcrumbSegment = TocBreadcrumbSegment | PdfBreadcrumbSegment

export function isTocBreadcrumbSegment(s: BreadcrumbSegment): s is TocBreadcrumbSegment {
  return 'tocEntry' in s
}

/**
 * Split a tab's tocPath into segment labels, dropping empty ones.
 *
 * An empty label can never match a TOC entry, so it would resolve to a segment
 * with no entry — rendering as a chevron with a blank label next to the book
 * title. Guard here rather than at the writers: the path is a plain string on the
 * tab and any in-place navigation can leave a malformed one behind.
 */
function splitTocPath(tocPath: string): string[] {
  return tocPath.split(' · ').filter((label) => label !== '')
}

/**
 * Given a flat TocEntry list and a segment label path, resolve each segment
 * to its TocEntry by walking the tree from root to leaf, matching by text at
 * each depth level.
 */
function resolveTocSegments(
  entries: TocEntry[],
  labels: string[],
): TocBreadcrumbSegment[] {
  if (!entries.length || !labels.length) return []

  // Build children map: parentId → TocEntry[]
  const childrenByParentId = new Map<number | null, TocEntry[]>()
  for (const entry of entries) {
    const key = entry.parentId ?? null
    if (!childrenByParentId.has(key)) childrenByParentId.set(key, [])
    childrenByParentId.get(key)!.push(entry)
  }

  const segments: TocBreadcrumbSegment[] = []
  let currentParentId: number | null = null

  for (let segmentIndex = 0; segmentIndex < labels.length; segmentIndex++) {
    const label = labels[segmentIndex]!
    const siblings: TocEntry[] = childrenByParentId.get(currentParentId) ?? []
    const matched: TocEntry | null = siblings.find((entry: TocEntry) => entry.text === label) ?? null
    const children = matched ? (childrenByParentId.get(matched.id) ?? []) : []

    segments.push({
      label,
      tocEntry: matched,
      siblings,
      children,
      isActive: segmentIndex === labels.length - 1,
    })

    if (matched == null) break
    currentParentId = matched.id
  }

  return segments
}

/**
 * Given a flat PdfOutlineEntry list and a " · "-separated path string,
 * resolve each segment to its PdfOutlineEntry with sibling lists.
 */
function resolvePdfSegments(
  entries: PdfOutlineEntry[],
  labels: string[],
): PdfBreadcrumbSegment[] {
  if (!entries.length || !labels.length) return []

  // Group entries by parentPath for sibling lookup.
  const byParentPath = new Map<string, PdfOutlineEntry[]>()
  for (const entry of entries) {
    if (!byParentPath.has(entry.parentPath)) byParentPath.set(entry.parentPath, [])
    byParentPath.get(entry.parentPath)!.push(entry)
  }

  const segments: PdfBreadcrumbSegment[] = []
  let currentParentPath = ''

  for (let segmentIndex = 0; segmentIndex < labels.length; segmentIndex++) {
    const label = labels[segmentIndex]!
    const siblings = byParentPath.get(currentParentPath) ?? []
    const matched = siblings.find((entry) => entry.text === label) ?? null
    const children = matched ? (byParentPath.get(matched.fullPath) ?? []) : []

    segments.push({
      label,
      outlineEntry: matched,
      siblings,
      children,
      isActive: segmentIndex === labels.length - 1,
    })

    if (matched == null) break
    currentParentPath = matched.fullPath
  }

  return segments
}

export interface TocRootEntry {
  id: number
  text: string
}

/**
 * Routes whose documents have a table of contents, and so whose tabs can carry a
 * meaningful `tocPath`. On every other route a tocPath means nothing — there is no
 * TOC for it to be a path into.
 */
const TOC_BEARING_ROUTES = ['/book-view', '/pdf-view']

export function useAppTitleBarTocBreadcrumb(
  activeTabRoute: () => string | undefined,
  activeTabTocPath: () => string | undefined,
  activeTabId: () => string,
  getTocBridge: (tabId: string) => TocBridge | null,
  getPdfBridge: (tabId: string) => PdfBridge | null,
): {
  segments: ComputedRef<BreadcrumbSegment[]>
  rootTocEntries: ComputedRef<TocEntry[]>
  rootPdfEntries: ComputedRef<PdfOutlineEntry[]>
  plainSegmentLabels: ComputedRef<string[]>
} {
  const segments = computed<BreadcrumbSegment[]>(() => {
    const route = activeTabRoute()
    const tocPath = activeTabTocPath()
    if (!tocPath) return []

    const currentTabId = activeTabId()

    if (route === '/book-view') {
      const bridge = getTocBridge(currentTabId)
      if (!bridge) return []
      const labels = splitTocPath(tocPath)
      return resolveTocSegments(bridge.tocEntries, labels)
    }

    if (route === '/pdf-view') {
      const bridge = getPdfBridge(currentTabId)
      if (!bridge) return []
      const labels = splitTocPath(tocPath)
      return resolvePdfSegments(bridge.outlineEntries, labels)
    }

    return []
  })

  /**
   * First-tier TocEntry items for the book-title dropdown on book-view tabs.
   * Empty when no bridge is registered or the TOC has no entries.
   */
  const rootTocEntries = computed<TocEntry[]>(() => {
    const route = activeTabRoute()
    if (route !== '/book-view') return []
    const bridge = getTocBridge(activeTabId())
    if (!bridge || !bridge.tocEntries.length) return []
    return bridge.tocEntries.filter((entry) => entry.parentId == null)
  })

  /**
   * First-tier PdfOutlineEntry items for the book-title dropdown on pdf-view tabs.
   * Empty when no bridge is registered or the outline has no entries.
   */
  const rootPdfEntries = computed<PdfOutlineEntry[]>(() => {
    const route = activeTabRoute()
    if (route !== '/pdf-view') return []
    const bridge = getPdfBridge(activeTabId())
    if (!bridge || !bridge.outlineEntries.length) return []
    return bridge.outlineEntries.filter((entry) => entry.parentPath === '')
  })

  /**
   * Labels for the non-interactive fallback the title bar renders while a TOC-bearing
   * tab has no bridge registered yet — the window between navigating to a book and its
   * view mounting, where the tab already knows its breadcrumb but nothing can resolve
   * it to entries. Empty on every other route, which is what keeps a tocPath from
   * outliving the document it describes.
   */
  const plainSegmentLabels = computed<string[]>(() => {
    const route = activeTabRoute()
    if (route === undefined || !TOC_BEARING_ROUTES.includes(route)) return []
    const tocPath = activeTabTocPath()
    return tocPath ? splitTocPath(tocPath) : []
  })

  return { segments, rootTocEntries, rootPdfEntries, plainSegmentLabels }
}
