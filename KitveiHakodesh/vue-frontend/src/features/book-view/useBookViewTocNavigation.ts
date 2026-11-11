/**
 * TOC-driven navigation for the book view.
 *
 * Owns:
 * - onTocSelect / onAltTocSelect — jump to a TOC entry
 * - navigateToAdjacentTocSection — Ctrl+Arrow keyboard section navigation
 *   with a lag-window guard so rapid consecutive presses use the just-navigated
 *   entry id rather than the stale activeTocEntryId (which the scroll sync
 *   updates asynchronously).
 * - altTocLabelMap — line index → the alt-TOC labels drawn in the text, from every alt
 *   structure (see toc/altTocTextLabels)
 */
import { computed, watch } from 'vue'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import type { TocEntry } from '@/webview-host/queries.types'
import { buildAltTocLineLabels } from './toc/altTocTextLabels'
import type { AltTocSection } from './toc/useBookViewToc'
import type { ScrollToLineOptions } from './lines/useBookViewLinesNavigation'
type LinesContentInstance = {
  scrollToLine: (lineIndex: number, options?: ScrollToLineOptions) => void
  scrollToLineId: (lineId: number, lineIndex?: number, options?: ScrollToLineOptions) => void
}

const SECTION_NAVIGATION_LAG_WINDOW_MS = 500

export function useBookViewTocNavigation(
  tocEntries: () => TocEntry[],
  activeTocEntryId: import('vue').Ref<number | undefined>,
  activeAltTocEntryId: import('vue').Ref<number | undefined>,
  linesContentRef: () => LinesContentInstance | null,
  getActiveTocEntry: (lineIndex: number) => TocEntry | null,
  getActiveAltTocEntry: (lineIndex: number) => TocEntry | null,
  getTocPath: (entry: TocEntry) => string,
  altTocSections: import('vue').Ref<AltTocSection[]>,
  altTocStructuresPrintingNumbers: import('vue').Ref<ReadonlySet<number>>,
  openTocEntryId: number | undefined,
) {
  const paneNavigation = usePaneNavigation()

  // Restore initial TOC entry when opening a book via a deep link (openTocEntryId is set).
  if (openTocEntryId != null) {
    const stopWatcher = watch(
      () => tocEntries(),
      (entries) => {
        if (!entries.length) return
        const entry = entries.find((e) => e.id === openTocEntryId)
        if (entry != null) {
          activeTocEntryId.value = entry.id
          paneNavigation.updateActiveTab({ tocPath: getTocPath(entry) })
        }
        stopWatcher()
      },
    )
  }

  const altTocLabelMap = computed(() =>
    buildAltTocLineLabels(altTocSections.value, altTocStructuresPrintingNumbers.value),
  )

  let lastSectionNavigationEntryId: number | null = null
  let lastSectionNavigationTimestamp = 0

  function onTocSelect(entry: TocEntry) {
    if (entry.lineId == null) return
    activeTocEntryId.value = entry.id
    paneNavigation.updateActiveTab({ tocPath: getTocPath(entry) })
    linesContentRef()?.scrollToLineId(entry.lineId, entry.lineIndex ?? undefined, { force: true })
    // Both trees show the same position, so a main click moves the alt highlight too.
    if (entry.lineIndex != null) {
      activeAltTocEntryId.value = getActiveAltTocEntry(entry.lineIndex)?.id
    }
  }

  function onAltTocSelect(entry: TocEntry) {
    if (entry.lineId == null) return
    activeAltTocEntryId.value = entry.id
    linesContentRef()?.scrollToLineId(entry.lineId, undefined, { force: true })
    if (entry.lineIndex != null) {
      const mainEntry = getActiveTocEntry(entry.lineIndex)
      if (mainEntry) {
        activeTocEntryId.value = mainEntry.id
        paneNavigation.updateActiveTab({ tocPath: getTocPath(mainEntry) })
      }
    }
  }

  function navigateToAdjacentTocSection(direction: 'next' | 'previous') {
    const entries = tocEntries()
    if (!entries.length) return

    const lagWindowActive =
      Date.now() - lastSectionNavigationTimestamp < SECTION_NAVIGATION_LAG_WINDOW_MS
    const effectiveEntryId =
      lagWindowActive && lastSectionNavigationEntryId != null
        ? lastSectionNavigationEntryId
        : activeTocEntryId.value

    const activeEntry = entries.find((e) => e.id === effectiveEntryId) ?? null
    const currentIndex = activeEntry != null ? entries.indexOf(activeEntry) : -1

    if (direction === 'next') {
      for (let i = currentIndex + 1; i < entries.length; i++) {
        const candidate = entries[i]!
        if (candidate.lineId != null && candidate.lineIndex != null) {
          lastSectionNavigationEntryId = candidate.id
          lastSectionNavigationTimestamp = Date.now()
          activeTocEntryId.value = candidate.id
          paneNavigation.updateActiveTab({ tocPath: getTocPath(candidate) })
          linesContentRef()?.scrollToLine(candidate.lineIndex, { force: true })
          return
        }
      }
    } else {
      const currentLineIndex = activeEntry?.lineIndex ?? null
      for (let i = currentIndex - 1; i >= 0; i--) {
        const candidate = entries[i]!
        if (
          candidate.lineId != null &&
          candidate.lineIndex != null &&
          (currentLineIndex == null || candidate.lineIndex < currentLineIndex)
        ) {
          lastSectionNavigationEntryId = candidate.id
          lastSectionNavigationTimestamp = Date.now()
          activeTocEntryId.value = candidate.id
          paneNavigation.updateActiveTab({ tocPath: getTocPath(candidate) })
          linesContentRef()?.scrollToLine(candidate.lineIndex, { force: true })
          return
        }
      }
    }
  }

  return { onTocSelect, onAltTocSelect, navigateToAdjacentTocSection, altTocLabelMap }
}
