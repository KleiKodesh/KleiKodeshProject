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
import type { TocEntry } from '@/features/book-view/toc/useBookViewToc'
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

export function useAppTitleBarTocBreadcrumb(
  activeTabRoute: () => string | undefined,
  activeTabTocPath: () => string | undefined,
  activeTabId: () => string,
  getTocBridge: (tabId: string) => TocBridge | null,
  getPdfBridge: (tabId: string) => PdfBridge | null,
): { segments: ComputedRef<BreadcrumbSegment[]> } {
  const segments = computed<BreadcrumbSegment[]>(() => {
    const route = activeTabRoute()
    const tocPath = activeTabTocPath()
    if (!tocPath) return []

    const currentTabId = activeTabId()

    if (route === '/book-view') {
      const bridge = getTocBridge(currentTabId)
      if (!bridge) return []
      const labels = tocPath.split(' / ')
      return resolveTocSegments(bridge.tocEntries, labels)
    }

    if (route === '/pdf-view') {
      const bridge = getPdfBridge(currentTabId)
      if (!bridge) return []
      const labels = tocPath.split(' · ')
      return resolvePdfSegments(bridge.outlineEntries, labels)
    }

    return []
  })

  return { segments }
}
