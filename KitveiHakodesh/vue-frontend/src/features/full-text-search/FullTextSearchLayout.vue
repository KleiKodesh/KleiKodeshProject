<script setup lang="ts">
import { ref } from 'vue'
import { useElementSize } from '@vueuse/core'
import PageCard from '@/components/PageCard.vue'

/**
 * The full-text search page's frame: the bar dock at the top, the clean-page options card,
 * the results column and the filter column beside it. Every page that searches with this
 * UI (the library search, the user-space search) fills the same frame through its slots,
 * so the layout lives once.
 *
 * Slots: `bar` (the search bar - moved into the card while it shows), `options` (the
 * options dropdown under the bar), `card-options` (the options inside the clean-page
 * card), `results` (the results column), `filter` (the filter panel), `overlay` (anything
 * over the whole page).
 */
const props = defineProps<{
  /** Clean page: the bar and the options show as a centered card. */
  showOptionsCard: boolean
  isOptionsOpen: boolean
  isFilterOpen: boolean
  /** Unique per page instance - names the card's slot the bar is teleported into. */
  instanceKey: string
}>()
const emit = defineEmits<{ closeFilter: [] }>()

const cardBarSlotId = `fts-card-bar-${props.instanceKey.replace(/[^\w-]/g, '_')}`

// The options dropdown fills the whole height under the bar, i.e. the results area's.
const resultsAreaRef = ref<HTMLElement | null>(null)
const { height: resultsAreaHeight } = useElementSize(resultsAreaRef)
</script>

<template>
  <div class="search-page">
    <!-- Hidden while the clean-page card holds the bar, so the empty dock takes no room. -->
    <!-- Height bound only while the dropdown is open: otherwise every resize frame re-rendered the page for it. -->
    <div v-show="!showOptionsCard" class="search-bar-dock" :style="isOptionsOpen ? { '--options-panel-height': `${resultsAreaHeight}px` } : undefined">
      <!-- On the clean page the bar lives inside the centered card; Teleport moves the SAME
           instance (no remount), so typing, focus and refs survive the hop back to the top. -->
      <Teleport defer :to="`#${cardBarSlotId}`" :disabled="!showOptionsCard">
        <slot name="bar" />
      </Teleport>
      <slot name="options" />
    </div>

    <div ref="resultsAreaRef" class="results-area">
      <!-- v-show, not v-if: the Teleport target must stay in the DOM while the bar moves out. -->
      <PageCard v-show="showOptionsCard">
        <div :id="cardBarSlotId" class="card-bar-slot" />
        <slot name="card-options" />
      </PageCard>
      <div v-if="!showOptionsCard" class="results-list-wrap">
        <slot name="results" />
      </div>

      <div v-if="isFilterOpen" class="filter-shell" :class="{ floating: showOptionsCard }" @click.self="emit('closeFilter')">
        <slot name="filter" />
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
  /* The results area has no stacking context, so its filter shell (z-index 10) competes
     with this dock directly: stay above it so the dock's popups (options panel, sort
     dropdown) paint over both the results list and the filter side panel. */
  z-index: 11;
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
/* Narrow overlay mode: shell covers the results area with a semitransparent backdrop */
.filter-shell {
  position: absolute;
  inset: 0;
  z-index: 10;
  background: rgba(0, 0, 0, 0.28);
}

/* Wide mode: shell is a transparent flex child; panel sits beside the results */
@media (min-width: 520px) {
  .results-area {
    flex-direction: row-reverse;
  }
  .results-list-wrap {
    min-width: 0;
  }
  /* Flex, so the panel inside can stretch to the shell's full height. As a plain block
     the panel had nothing to stretch against and collapsed to its content height. */
  .filter-shell {
    position: static;
    background: none;
    height: 100%;
    flex-shrink: 0;
    display: flex;
  }
  /* Over the clean-page card the panel floats instead of taking a column, so the
     centered card is not pushed aside. */
  .filter-shell.floating {
    position: absolute;
    inset-block: 0;
    inset-inline-start: 0;
  }
  /* `height: 100%` would resolve against the shell and then ADD the panel's own margin,
     overflowing the area by that much at top and bottom. Stretching instead lets the
     margin be a true inset: the panel fills the height that's left over. */
  .results-area :deep(.panel) {
    position: static;
    align-self: stretch;
    flex-shrink: 0;
  }
}
</style>
