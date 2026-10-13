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

const DROPDOWN_ONLY_KEYS = new Set<HomeDailyLearningItem['key']>([
  'rambam-1',
  'rambam-3',
  'kitzur-shulchan-aruch',
  'arukh-hashulchan-yomi',
  'perek-yomi',
  'pirkei-avot',
  'daf-weekly',
])

interface ResolvedDailyLearningItem extends HomeDailyLearningItem {
  resolved: HomeDailyLearningResolvedTarget
}

/** One of today's cycles; `open` is null when its book is not in the library. */
export interface HomeDailyLearningEntry {
  key: string
  label: string
  location: string
  open: ((openInNewTab: boolean) => void) | null
}

/**
 * Today's learning cards (see homeDailyLearningItems.ts for the list),
 * each resolved BY NAME against the library catalog. Items whose book is not in
 * the library are left out, so the home collection just shows fewer cards.
 */
export function useHomeDailyLearning() {
  return useDailyLearningState().cards
}

/** Every cycle for today, including ones whose book is missing (not navigable). */
export function useHomeDailyLearningEntries() {
  return useDailyLearningState().entries
}

function useDailyLearningState() {
  const paneNavigation = usePaneNavigation()
  const booksDataStore = useBooksDataStore()
  const now = useNow({ interval: 60_000 })
  // Recompute only when the civil date changes, not every minute tick.
  const todayKey = computed(() => now.value.toDateString())
  const allItems = shallowRef<Array<HomeDailyLearningItem & { resolved: HomeDailyLearningResolvedTarget | null }>>([])
  const resolvedItems = computed(() =>
    allItems.value.filter((item): item is ResolvedDailyLearningItem => item.resolved !== null),
  )
  let generation = 0

  watch(
    [todayKey, dbReady],
    async () => {
      const currentGeneration = ++generation
      if (!dbReady.value) return
      const items = await computeHomeDailyLearningItems(new Date())
      const withTargets: Array<HomeDailyLearningItem & { resolved: HomeDailyLearningResolvedTarget | null }> = []
      for (const item of items) {
        let resolved: HomeDailyLearningResolvedTarget | null = null
        try {
          resolved = (await resolveHomeDailyLearningTarget(item.target)) ?? null
        } catch {}
        withTargets.push({ ...item, resolved })
      }
      if (currentGeneration === generation) allItems.value = withTargets
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
  const entries = computed<HomeDailyLearningEntry[]>(() =>
    allItems.value.map((item) => {
      const resolvedItem = item.resolved ? (item as ResolvedDailyLearningItem) : null
      return {
        key: item.key,
        label: item.label,
        location: item.location,
        open: resolvedItem ? (openInNewTab: boolean) => open(resolvedItem, openInNewTab) : null,
      }
    }),
  )

  // Cycles added for the date-bar dropdown only; the home cards keep their original set.
  const cards = computed<HomeSuggestionCard[]>(() =>
    resolvedItems.value.filter((item) => !DROPDOWN_ONLY_KEYS.has(item.key)).map((item) => ({
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
  return { cards, entries }
}
