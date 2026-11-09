import { computed } from 'vue'
import { useHomeHistoryCards, type HomeHistoryCardActions } from './useHomeHistoryCards'
import { useHomeDailyLearning } from './useHomeDailyLearning'
import { useHomeNavCards } from './useHomeNavCards'

/** Below this many history cards, daily-learning cards fill the gap. */
const FILLED_CARD_COUNT = 9

/**
 * The home page's one card collection. Nav rail hidden: navigation
 * destinations, then history. Nav rail shown: history, then daily-learning
 * placeholders padding it up to FILLED_CARD_COUNT.
 */
export function useHomeSuggestionCards(
  historyActions: HomeHistoryCardActions,
  navigate: (label: string) => unknown,
) {
  const historyCards = useHomeHistoryCards(historyActions)
  const dailyLearningCards = useHomeDailyLearning()
  const navCards = useHomeNavCards(navigate)
  const cards = computed(() => {
    // Nav rail hidden → nav cards fill the page; daily learning is dropped.
    if (navCards.value.length) return [...navCards.value, ...historyCards.cards.value]
    // Daily learning only pads a short history up to FILLED_CARD_COUNT.
    const room = Math.max(0, FILLED_CARD_COUNT - historyCards.cards.value.length)
    return [...historyCards.cards.value, ...dailyLearningCards.cards.value.slice(0, room)]
  })
  // The whole collection is known: show it in one step, so the centered search bar
  // never jumps as cards trickle in. Daily learning only matters when it can show.
  const ready = computed(
    () =>
      historyCards.loaded.value &&
      (navCards.value.length > 0 || dailyLearningCards.settled.value),
  )
  return { cards, ready }
}
