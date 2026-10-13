import { computed, shallowRef, watch } from 'vue'
import { documentIcon } from '@/utils/documentIcons'
import { useNow } from '@vueuse/core'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { dbReady } from '@/webview-host/seforimDb'
import { computeHomeDailyLearningItems } from './homeDailyLearningItems'
import { resolveHomeDailyLearningTarget, type HomeDailyLearningResolvedTarget } from './homeDailyLearningResolver'
import type { HomeDailyLearningItem } from './homeDailyLearningTypes'
import type { HomeSuggestionCard } from './homeSuggestionCard'
import { bookResumePath } from './homeResumePath'
import { useBooksDataStore } from '@/stores/booksDataStore'

interface ResolvedDailyLearningItem extends HomeDailyLearningItem {
  resolved: HomeDailyLearningResolvedTarget
}

/**
 * Today's learning cards (see homeDailyLearningItems.ts for the list),
 * each resolved BY NAME against the library catalog. Items whose book is not in
 * the library are left out, so the home collection just shows fewer cards.
 */
export function useHomeDailyLearning() {
  const paneNavigation = usePaneNavigation()
  const booksDataStore = useBooksDataStore()
  const now = useNow({ interval: 60_000 })
  // Recompute only when the civil date changes, not every minute tick.
  const todayKey = computed(() => now.value.toDateString())
  const resolvedItems = shallowRef<ResolvedDailyLearningItem[]>([])
  let generation = 0

  watch(
    [todayKey, dbReady],
    async () => {
      const currentGeneration = ++generation
      if (!dbReady.value) return
      const items = await computeHomeDailyLearningItems(new Date())
      const resolvedCards: ResolvedDailyLearningItem[] = []
      for (const item of items) {
        try {
          const resolved = await resolveHomeDailyLearningTarget(item.target)
          if (resolved) resolvedCards.push({ ...item, resolved })
        } catch {}
      }
      if (currentGeneration === generation) resolvedItems.value = resolvedCards
    },
    { immediate: true },
  )

  function open(item: ResolvedDailyLearningItem, openInNewTab: boolean) {
    const { resolved } = item
    paneNavigation.openOrUpdateActiveTab(
      {
        route: '/book-view',
        title: resolved.bookTitle,
        bookId: resolved.bookId,
        openTocEntryId: resolved.tocEntryId,
        openTocLineIndex: resolved.tocLineIndex,
      },
      openInNewTab,
    )
  }

  const icon = documentIcon('book')
  return computed<HomeSuggestionCard[]>(() =>
    resolvedItems.value.map((item) => ({
      key: `daily:${item.key}`,
      // Shown exactly like a history card: no cycle label, just book + path.
      title: item.resolved.bookTitle,
      path: bookResumePath(
        booksDataStore.allBooksMap.get(item.resolved.bookId),
        item.resolved.bookTitle,
        item.location,
      ),
      icon: icon.icon24,
      color: icon.color,
      open: (openInNewTab) => open(item, openInNewTab),
    })),
  )
}
