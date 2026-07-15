import { ref, watch } from 'vue'
import { getTocPathsForLines, getEnclosingTocPathForLineRanges } from '@/webview-host/seforimApi'

/**
 * Fetches and caches TOC paths for commentary groups. Keyed by bookId — resolved
 * asynchronously after groups load, never blocks rendering.
 *
 * In single-line mode the path comes from the first line of each group (the existing
 * GET_TOC_PATHS_FOR_LINES query).
 *
 * In section mode (isSectionMode = true) every group spans many source lines, so the
 * first line's path would only reflect the first verse. Instead we find the deepest
 * common ancestor TOC entry that covers both the first and last line of each group —
 * this gives the enclosing section label (e.g. "פרק א" instead of "פרק א · משנה א").
 */
export function useCommentaryTocPaths(
  groups: () => any[],
  isSectionMode: () => boolean,
) {
  const commentaryTocPaths = ref<Map<number, string>>(new Map())

  async function fetchSingleLineTocPaths(groupList: any[]) {
    const lineIds = groupList
      .map((g) => g.lines[0]?.lineId)
      .filter((id): id is number => id != null && id > 0)
    if (!lineIds.length) return

    const rows = await getTocPathsForLines(lineIds)
    const pathsByLineId = new Map(rows.map((r) => [r.lineId, r.tocPath]))
    const resolved = new Map<number, string>()
    for (const g of groupList) {
      const lineId = g.lines[0]?.lineId
      if (lineId != null && lineId > 0) {
        const tocPath = pathsByLineId.get(lineId)
        if (tocPath) resolved.set(g.bookId, tocPath)
      }
    }
    commentaryTocPaths.value = resolved
  }

  async function fetchSectionTocPaths(groupList: any[]) {
    // Build (groupKey, firstLineId, lastLineId) triples.
    // Skip placeholder groups (lineId === -1) and groups with only one real line
    // (for those, fall back to the single-line path since first === last).
    const triples: Array<{ groupKey: number; firstLineId: number; lastLineId: number; bookId: number }> = []
    const singleLineGroups: Array<{ groupKey: number; lineId: number; bookId: number }> = []

    for (let index = 0; index < groupList.length; index++) {
      const g = groupList[index]
      const realLines = g.lines.filter((l: any) => l.lineId > 0)
      if (realLines.length === 0) continue

      if (realLines.length === 1) {
        singleLineGroups.push({ groupKey: index, lineId: realLines[0].lineId, bookId: g.bookId })
      } else {
        const firstLineId = realLines[0].lineId
        const lastLineId = realLines[realLines.length - 1].lineId
        triples.push({ groupKey: index, firstLineId, lastLineId, bookId: g.bookId })
      }
    }

    const resolved = new Map<number, string>()

    // Batch the common-ancestor query for all multi-line groups.
    if (triples.length > 0) {
      const params: number[] = []
      for (const { groupKey, firstLineId, lastLineId } of triples) {
        params.push(groupKey, firstLineId, lastLineId)
      }
      const rows = await getEnclosingTocPathForLineRanges(params)
      const pathsByGroupKey = new Map(rows.map((r) => [r.groupKey, r.tocPath]))
      for (const { groupKey, bookId } of triples) {
        const tocPath = pathsByGroupKey.get(groupKey)
        if (tocPath) resolved.set(bookId, tocPath)
      }
    }

    // Fall back to the single-line query for groups with only one real line.
    if (singleLineGroups.length > 0) {
      const lineIds = singleLineGroups.map((g) => g.lineId)
      const rows = await getTocPathsForLines(lineIds)
      const pathsByLineId = new Map(rows.map((r) => [r.lineId, r.tocPath]))
      for (const { lineId, bookId } of singleLineGroups) {
        const tocPath = pathsByLineId.get(lineId)
        if (tocPath) resolved.set(bookId, tocPath)
      }
    }

    commentaryTocPaths.value = resolved
  }

  watch(
    groups,
    (newGroups) => {
      commentaryTocPaths.value = new Map()
      if (!newGroups.length) return
      if (isSectionMode()) {
        void fetchSectionTocPaths(newGroups)
      } else {
        void fetchSingleLineTocPaths(newGroups)
      }
    },
    { flush: 'post', immediate: true },
  )

  return {
    commentaryTocPaths,
  }
}
