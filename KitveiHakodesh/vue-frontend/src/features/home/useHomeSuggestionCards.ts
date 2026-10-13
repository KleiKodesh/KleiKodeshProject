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
  return computed(() => {
    // Nav rail hidden → nav cards fill the page; daily learning is dropped.
    if (navCards.value.length) return [...navCards.value, ...historyCards.value]
    // Daily learning only pads a short history up to FILLED_CARD_COUNT.
    const room = Math.max(0, FILLED_CARD_COUNT - historyCards.value.length)
    return [...historyCards.value, ...dailyLearningCards.value.slice(0, room)]
  })
}
