<script setup lang="ts">
import { computed, ref } from 'vue'
import { useElementBounding } from '@vueuse/core'
import PageCard from '@/components/PageCard.vue'

/**
 * The full-text search page's frame: the bar dock at the top, the clean-page options card,
 * and the results. Every page that searches with this UI (the library search, the
 * user-space search) fills the same frame through its slots, so the layout lives once.
 *
 * Slots: `bar` (the search bar - moved into the card while it shows), `options` (the
 * options dropdown under the bar), `card-options` (the options inside the clean-page
 * card), `results` (the results column), `filter` (the filter dropdown under the bar -
 * it travels with the bar), `overlay` (anything over the whole page).
 */
const props = defineProps<{
  /** Clean page: the bar and the options show as a centered card. */
  showOptionsCard: boolean
  isOptionsOpen: boolean
  isFilterOpen: boolean
  /** Unique per page instance - names the card's slot the bar is teleported into. */
  instanceKey: string
}>()

const cardBarSlotId = `fts-card-bar-${props.instanceKey.replace(/[^\w-]/g, '_')}`

// The options and filter dropdowns fill the whole height under the bar, i.e. the results area's.
const resultsAreaRef = ref<HTMLElement | null>(null)
const { height: resultsAreaHeight, bottom: resultsAreaBottom } = useElementBounding(resultsAreaRef)

// Inside the clean-page card the filter dropdown starts under the bar wherever the card
// puts it, so its height is what is left from there to the bottom of the results area.
const cardBarSlotRef = ref<HTMLElement | null>(null)
const { bottom: cardBarSlotBottom } = useElementBounding(cardBarSlotRef)
const CARD_BAR_SLOT_PADDING = 4
const FILTER_BOTTOM_GAP = 8
const cardFilterHeight = computed(() =>
  Math.max(0, resultsAreaBottom.value - (cardBarSlotBottom.value - CARD_BAR_SLOT_PADDING) - FILTER_BOTTOM_GAP))
</script>

<template>
  <div class="search-page">
    <!-- Hidden while the clean-page card holds the bar, so the empty dock takes no room. -->
    <!-- Height bound only while the dropdown is open: otherwise every resize frame re-rendered the page for it. -->
    <div v-show="!showOptionsCard" class="search-bar-dock" :style="isOptionsOpen || isFilterOpen ? { '--options-panel-height': `${resultsAreaHeight}px` } : undefined">
      <!-- On the clean page the bar lives inside the centered card; Teleport moves the SAME
           instance (no remount), so typing, focus and refs survive the hop back to the top.
           The filter dropdown hangs from the bar, so it moves with it. -->
      <Teleport defer :to="`#${cardBarSlotId}`" :disabled="!showOptionsCard">
        <slot name="bar" />
        <slot v-if="isFilterOpen" name="filter" />
      </Teleport>
      <slot name="options" />
    </div>

    <div ref="resultsAreaRef" class="results-area">
      <!-- v-show, not v-if: the Teleport target must stay in the DOM while the bar moves out. -->
      <PageCard v-show="showOptionsCard">
        <!-- Height bound only while the dropdown is open, like the dock's. -->
        <div
          :id="cardBarSlotId"
          ref="cardBarSlotRef"
          class="card-bar-slot"
          :style="isFilterOpen && showOptionsCard ? { '--card-filter-height': `${cardFilterHeight}px` } : undefined"
        />
        <slot name="card-options" />
      </PageCard>
      <div v-if="!showOptionsCard" class="results-list-wrap">
        <slot name="results" />
      </div>
    </div>

    <slot name="overlay" />
  </div>
</template>

<style scoped>
.search-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  position: relative;
  background: var(--bg-primary);
}
.results-area {
  flex: 1;
  min-height: 0;
  position: relative;
  display: flex;
  flex-direction: column;
}
/* Anchor for the options panel: on wide screens it floats as a popup below
   the search bar, so it needs the bar's bottom edge as its containing block. */
.search-bar-dock {
  position: relative;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  min-height: 0;
  /* Above the results area, so the dock's popups (options panel, filter and sort
     dropdowns) paint over the results list. */
  z-index: 11;
}
/* Inside the clean-page card the filter dropdown has no dock to hang from. With no
   offsets at all it starts where it sits in flow, right under the bar, and its
   containing block stays the results area (the nearest positioned ancestor, outside the
   card), so the card's own clipping does not cut it. The height is measured above. */
.card-bar-slot :deep(.panel) {
  top: auto;
  inset-inline-start: auto;
  margin-inline-start: 8px;
  height: var(--card-filter-height, 60vh);
}
/* Room around the bar inside the card (TopSearchBar's own 8px/6px is tuned for the
   page edge, too tight in the card). */
.card-bar-slot {
  flex-shrink: 0;
  padding: 4px 0;
}
/* The card owns the frame; the panel inside is just its lower section. */
.results-area :deep(.options-panel.as-card) {
  width: 100%;
  max-width: 100%;
  min-height: 0;
  flex: 1 1 auto;
  border: none;
  border-radius: 0;
  box-shadow: none;
}
/* Full-width card (PageCard <=600px) takes the app background; the panel must
   not paint its own bg-secondary over it. Breakpoint mirrors PageCard.vue. */
@media (max-width: 600px) {
  .results-area :deep(.options-panel.as-card),
  .results-area :deep(.options-panel.as-card .panel-header) {
    background: none;
  }
}
.results-list-wrap {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
</style>
