import { ref, watch } from 'vue'
import { getTocPathsForLines, getEnclosingTocPathForLineRanges } from '@/webview-host/seforimApi'
import { commentaryGroupKey } from './useCommentary'

/**
 * Fetches and caches TOC paths for commentary groups. Keyed by group identity
 * (see commentaryGroupKey) — NOT by bookId, since one book can produce several
 * groups whose differing line subsets resolve to different TOC paths. Resolved
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
  const commentaryTocPaths = ref<Map<string, string>>(new Map())

  async function fetchSingleLineTocPaths(groupList: any[]) {
    const lineIds = groupList
      .map((g) => g.lines[0]?.lineId)
      .filter((id): id is number => id != null && id > 0)
    if (!lineIds.length) return

    const rows = await getTocPathsForLines(lineIds)
    const pathsByLineId = new Map(rows.map((r) => [r.lineId, r.tocPath]))
    const resolved = new Map<string, string>()
    for (const g of groupList) {
      const lineId = g.lines[0]?.lineId
      if (lineId != null && lineId > 0) {
        const tocPath = pathsByLineId.get(lineId)
        if (tocPath) resolved.set(commentaryGroupKey(g), tocPath)
      }
    }
    commentaryTocPaths.value = resolved
  }

  async function fetchSectionTocPaths(groupList: any[]) {
    // Build (groupKey, firstLineId, lastLineId) triples.
    // Skip placeholder groups (lineId === -1) and groups with only one real line
    // (for those, fall back to the single-line path since first === last).
    const triples: Array<{ groupKey: number; firstLineId: number; lastLineId: number; key: string }> = []
    const singleLineGroups: Array<{ groupKey: number; lineId: number; key: string }> = []

    for (let index = 0; index < groupList.length; index++) {
      const g = groupList[index]
      const realLines = g.lines.filter((l: any) => l.lineId > 0)
      if (realLines.length === 0) continue

      if (realLines.length === 1) {
        singleLineGroups.push({ groupKey: index, lineId: realLines[0].lineId, key: commentaryGroupKey(g) })
      } else {
        // Endpoints by min/max of lineId, NOT array position: g.lines is ordered
        // by lineIndex (per-book position) while these are line.id (global row
        // id), and the two orders need not agree. Reading [0] / [last] can hand
        // the query a non-enclosing pair, which resolves to a too-deep entry.
        let firstLineId = realLines[0].lineId
        let lastLineId = realLines[0].lineId
        for (const l of realLines) {
          if (l.lineId < firstLineId) firstLineId = l.lineId
          if (l.lineId > lastLineId) lastLineId = l.lineId
        }
        triples.push({ groupKey: index, firstLineId, lastLineId, key: commentaryGroupKey(g) })
      }
    }

    const resolved = new Map<string, string>()

    // Batch the common-ancestor query for all multi-line groups.
    if (triples.length > 0) {
      const params: number[] = []
      for (const { groupKey, firstLineId, lastLineId } of triples) {
        params.push(groupKey, firstLineId, lastLineId)
      }
      const rows = await getEnclosingTocPathForLineRanges(params)
      const pathsByGroupKey = new Map(rows.map((r) => [r.groupKey, r.tocPath]))
      for (const { groupKey, key } of triples) {
        const tocPath = pathsByGroupKey.get(groupKey)
        if (tocPath) resolved.set(key, tocPath)
      }
    }

    // Fall back to the single-line query for groups with only one real line.
    if (singleLineGroups.length > 0) {
      const lineIds = singleLineGroups.map((g) => g.lineId)
      const rows = await getTocPathsForLines(lineIds)
      const pathsByLineId = new Map(rows.map((r) => [r.lineId, r.tocPath]))
      for (const { lineId, key } of singleLineGroups) {
        const tocPath = pathsByLineId.get(lineId)
        if (tocPath) resolved.set(key, tocPath)
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
