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

/** Opens one cycle's reading for any date (calendar page); no-op when its book is not in the library. */
export function useDailyLearningOpener() {
  const paneNavigation = usePaneNavigation()
  return async (date: Date, key: HomeDailyLearningItem['key'], openInNewTab: boolean) => {
    if (!dbReady.value) return
    const item = (await computeHomeDailyLearningItems(date)).find((i) => i.key === key)
    if (!item) return
    const resolved = await resolveHomeDailyLearningTarget(item.target).catch(() => null)
    if (!resolved) return
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
}

type DailyLearningItemWithTarget = HomeDailyLearningItem & { resolved: HomeDailyLearningResolvedTarget | null }

// Today's resolved items, shared by the date bar and the cards and kept across home remounts.
let todayItems: { dateKey: string; items: DailyLearningItemWithTarget[] } | null = null
let todayItemsPending: { dateKey: string; promise: Promise<DailyLearningItemWithTarget[]> } | null = null
// Bumped when the library goes away, so a load still in flight can't cache its stale ids.
let libraryEpoch = 0

function loadTodayItems(dateKey: string): Promise<DailyLearningItemWithTarget[]> {
  if (todayItemsPending?.dateKey === dateKey) return todayItemsPending.promise
  const epoch = libraryEpoch
  const promise = (async () => {
    const items = await computeHomeDailyLearningItems(new Date())
    // All cycles in parallel: each one is several service round-trips.
    const withTargets = await Promise.all(
      items.map(async (item) => ({
        ...item,
        resolved: (await resolveHomeDailyLearningTarget(item.target).catch(() => null)) ?? null,
      })),
    )
    // Cleared meanwhile (library went away): don't put the old library's ids back.
    if (epoch === libraryEpoch) todayItems = { dateKey, items: withTargets }
    return withTargets
  })()
  todayItemsPending = { dateKey, promise }
  promise.catch(() => {
    if (todayItemsPending?.promise === promise) todayItemsPending = null
  })
  return promise
}

function useDailyLearningState() {
  const paneNavigation = usePaneNavigation()
  const booksDataStore = useBooksDataStore()
  const now = useNow({ interval: 60_000 })
  // Recompute only when the civil date changes, not every minute tick.
  const todayKey = computed(() => now.value.toDateString())
  const allItems = shallowRef<DailyLearningItemWithTarget[]>(
    todayItems?.dateKey === todayKey.value ? todayItems.items : [],
  )
  const resolvedItems = computed(() =>
    allItems.value.filter((item): item is ResolvedDailyLearningItem => item.resolved !== null),
  )
  let generation = 0

  watch(
    [todayKey, dbReady],
    async () => {
      const currentGeneration = ++generation
      if (!dbReady.value) {
        // Library going away (swap/update): its book ids may not survive it.
        todayItems = null
        todayItemsPending = null
        libraryEpoch++
        return
      }
      const cached = todayItems?.dateKey === todayKey.value ? todayItems.items : null
      if (cached) {
        allItems.value = cached
        return
      }
      const items = await loadTodayItems(todayKey.value)
      if (currentGeneration === generation) allItems.value = items
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
