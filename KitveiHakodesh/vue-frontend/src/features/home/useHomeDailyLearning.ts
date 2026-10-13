import { computed, shallowRef, watch } from 'vue'
import { useNow } from '@vueuse/core'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { dbReady } from '@/webview-host/seforimDb'
import { computeHomeDailyLearningItems } from './homeDailyLearningItems'
import { resolveHomeDailyLearningTarget, type HomeDailyLearningResolvedTarget } from './homeDailyLearningResolver'
import type { HomeDailyLearningItem } from './homeDailyLearningTypes'

export interface HomeDailyLearningCard extends HomeDailyLearningItem {
  resolved: HomeDailyLearningResolvedTarget
}

/**
 * Today's learning cards (see homeDailyLearningItems.ts for the list),
 * each resolved BY NAME against the library catalog. Items whose book is not in
 * the library are left out, so the row just shows fewer cards.
 */
export function useHomeDailyLearning() {
  const paneNavigation = usePaneNavigation()
  const now = useNow({ interval: 60_000 })
  // Recompute only when the civil date changes, not every minute tick.
  const todayKey = computed(() => now.value.toDateString())
  const cards = shallowRef<HomeDailyLearningCard[]>([])
  let generation = 0

  watch(
    [todayKey, dbReady],
    async () => {
      const currentGeneration = ++generation
      if (!dbReady.value) return
      const items = await computeHomeDailyLearningItems(new Date())
      const resolvedCards: HomeDailyLearningCard[] = []
      for (const item of items) {
        try {
          const resolved = await resolveHomeDailyLearningTarget(item.target)
          if (resolved) resolvedCards.push({ ...item, resolved })
        } catch {}
      }
      if (currentGeneration === generation) cards.value = resolvedCards
    },
    { immediate: true },
  )

  function openCard(card: HomeDailyLearningCard) {
    const { resolved } = card
    paneNavigation.updateActiveTab({
      route: '/book-view',
      title: resolved.bookTitle,
      bookId: resolved.bookId,
      openTocEntryId: resolved.tocEntryId,
      openTocLineIndex: resolved.tocLineIndex,
    })
  }

  return { cards, openCard }
}
