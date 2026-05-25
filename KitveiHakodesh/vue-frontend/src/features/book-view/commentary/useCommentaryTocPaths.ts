import { ref, watch } from 'vue'
import { query } from '@/webview-host/seforimDb'
import { SQL } from '@/webview-host/queries.sql'

/**
 * Fetches and caches TOC paths for commentary groups. Keyed by bookId — resolved
 * asynchronously after groups load, never blocks rendering.
 */
export function useCommentaryTocPaths(groups: () => any[]) {
  const commentaryTocPaths = ref<Map<number, string>>(new Map())

  async function fetchCommentaryTocPaths(groupList: any[]) {
    if (!groupList.length) return
    const lineIds = groupList
      .map((g) => g.lines[0]?.lineId)
      .filter((id): id is number => id != null)
    if (!lineIds.length) return
    const rows = await query<{ lineId: number; bookId: number; tocPath: string }>(
      SQL.GET_TOC_PATHS_FOR_LINES(lineIds.length),
      lineIds,
    )
    const pathsByLineId = new Map(rows.map((r) => [r.lineId, r.tocPath]))
    const resolved = new Map<number, string>()
    for (const g of groupList) {
      const lineId = g.lines[0]?.lineId
      if (lineId != null) {
        const tocPath = pathsByLineId.get(lineId)
        if (tocPath) resolved.set(g.bookId, tocPath)
      }
    }
    commentaryTocPaths.value = resolved
  }

  watch(
    groups,
    (newGroups) => {
      commentaryTocPaths.value = new Map()
      void fetchCommentaryTocPaths(newGroups)
    },
    { flush: 'post', immediate: true },
  )

  return {
    commentaryTocPaths,
  }
}
