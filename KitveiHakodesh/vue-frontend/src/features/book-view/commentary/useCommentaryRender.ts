import { computed, ref, watch } from 'vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { storeToRefs } from 'pinia'
import { applyDiacriticsFilter, removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import { censorDivineNames } from '@/utils/censorDivineNames'

/**
 * Manages content rendering for commentary lines: diacritics filtering, divine name censoring,
 * search highlighting, and render caching to avoid re-running expensive DOM operations.
 */
export function useCommentaryRender(groups: () => any[]) {
  const settingsStore = useSettingsStore()
  const { zoom } = storeToRefs(useBookViewStore())

  const diacriticsState = computed(() => settingsStore.diacriticsState)
  const commentaryFontPx = computed(() => {
    const effectiveFontSize = settingsStore.useSeparateCommentarySettings
      ? settingsStore.commentaryFontSize
      : settingsStore.fontSize
    return (zoom.value / 100) * (effectiveFontSize / 100) * 15
  })

  // Cache rendered HTML per flat index — avoids re-running applyDiacriticsFilter (DOM TreeWalker)
  // and censorDivineNames (6 regexes) on every render cycle for unchanged commentary lines.
  const renderCache = new Map<number, string>()
  let renderCacheKey = ''

  function getRenderCacheKey(
    searchQuery: string | undefined,
    currentMatchFlatIndex: number | undefined,
    currentMatchOccurrence: number | undefined,
  ): string {
    return `${diacriticsState.value}|${settingsStore.censorDivineNames}|${searchQuery ?? ''}|${currentMatchFlatIndex ?? -1}|${currentMatchOccurrence ?? 0}`
  }

  function highlightMatches(
    content: string,
    query: string,
    isCurrent: boolean,
    currentOccurrence: number,
  ): string {
    const q = removeDiacriticsForSearch(query.trim())
    if (!q) return content
    const stripped = removeDiacriticsForSearch(content.replace(/<[^>]*>/g, ''))
    if (!stripped.includes(q)) return content

    const matchStarts = new Set<number>()
    let idx = 0
    while ((idx = stripped.indexOf(q, idx)) !== -1) {
      matchStarts.add(idx)
      idx++
    }

    const out: string[] = []
    let strippedPos = 0,
      inTag = false,
      inMatch = false,
      matchCount = 0,
      matchOccurrence = 0
    for (let i = 0; i < content.length; i++) {
      const ch = content[i]!
      if (ch === '<') {
        inTag = true
        out.push(ch)
        continue
      }
      if (ch === '>') {
        inTag = false
        out.push(ch)
        continue
      }
      if (inTag) {
        out.push(ch)
        continue
      }
      const isDiacritic = /[\u0591-\u05C7]/.test(ch)
      if (!isDiacritic && matchStarts.has(strippedPos) && !inMatch) {
        out.push(
          `<mark class="search-match${isCurrent && matchOccurrence === currentOccurrence ? ' current' : ''}">`,
        )
        inMatch = true
        matchCount = 0
      }
      out.push(ch)
      if (!isDiacritic) {
        if (inMatch && ++matchCount === q.length) {
          out.push('</mark>')
          inMatch = false
          matchOccurrence++
        }
        strippedPos++
      }
    }
    return out.join('')
  }

  function renderContent(
    content: string,
    flatIndex: number,
    searchQuery: string | undefined,
    currentMatchFlatIndex: number | undefined,
    currentMatchOccurrence: number | undefined,
  ): string {
    const key = getRenderCacheKey(searchQuery, currentMatchFlatIndex, currentMatchOccurrence)
    if (key !== renderCacheKey) {
      renderCache.clear()
      renderCacheKey = key
    }
    const cached = renderCache.get(flatIndex)
    if (cached !== undefined) return cached

    let result =
      diacriticsState.value === 0 ? content : applyDiacriticsFilter(content, diacriticsState.value)
    if (settingsStore.censorDivineNames) result = censorDivineNames(result)
    if (searchQuery?.trim())
      result = highlightMatches(
        result,
        searchQuery,
        flatIndex === currentMatchFlatIndex,
        currentMatchOccurrence ?? 0,
      )

    renderCache.set(flatIndex, result)
    return result
  }

  // Invalidate render cache when groups change (new line content loaded)
  watch(
    groups,
    () => {
      renderCache.clear()
      renderCacheKey = ''
    },
    { flush: 'sync' },
  )

  return {
    diacriticsState,
    commentaryFontPx,
    renderContent,
  }
}
