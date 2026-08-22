<script setup lang="ts">
import { computed } from 'vue'
import {
  IconTextBulletList20Regular,
  IconGrid20Regular,
  IconTextBulletListTree20Regular,
  IconHome16Regular,
  IconSearch20Regular,
} from '@iconify-prerendered/vue-fluent'
import BookCatalogBreadcrumb from './BookCatalogBreadcrumb.vue'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'
import type { Component } from 'vue'

type BooksView = 'list' | 'tiles' | 'tree'

const props = defineProps<{
  view: BooksView
  path: CategoryNode[]
  isSearching: boolean
  /** Which face the bar is showing: the search field, or the path. */
  showSearch: boolean
}>()
const emit = defineEmits<{
  setView: [BooksView]
  navigate: [number]
  navigateToSibling: [{ atIndex: number; node: CategoryNode }]
  reset: []
  openSearch: []
  goHome: []
}>()

// Ordered: the toggle steps through this list and wraps, so the order here IS the
// cycle order.
const VIEWS: { value: BooksView; label: string; icon: Component; flip?: boolean }[] = [
  { value: 'list', label: 'תצוגת רשימה', icon: IconTextBulletList20Regular },
  { value: 'tiles', label: 'תצוגת אריחים', icon: IconGrid20Regular },
  { value: 'tree', label: 'תצוגת עץ', icon: IconTextBulletListTree20Regular, flip: true },
]

const currentIndex = computed(() => VIEWS.findIndex((v) => v.value === props.view))
const current = computed(() => VIEWS[currentIndex.value] ?? VIEWS[0]!)
const next = computed(() => VIEWS[(currentIndex.value + 1) % VIEWS.length]!)

function cycleView() {
  emit('setView', next.value.value)
}

// Home means "back to the top", which differs by view: the browse views walk a
// path, so it returns to the root; the tree has no path, so it collapses instead.
// Emitting the wrong one leaves the button dead — the tree view never reads
// `path`, so a navigate would do nothing there.
function goHome() {
  if (props.view === 'tree' && !props.isSearching) emit('reset')
  else emit('goHome')
}

// One button, three states: it shows the view you are IN and steps to the next
// one on click, wrapping round. The title names the view being switched TO, since
// the icon already says where you are.
const viewToggleTitle = computed(() => `${current.value.label} — החלף ל${next.value.label}`)

// The path face has nothing to draw in tree view — the expanded tree already
// shows where you are — so there the bar keeps only its home button, which
// collapses the tree rather than walking a path.
const showBreadcrumb = computed(() => props.view !== 'tree' || props.isSearching)
</script>

<template>
  <div class="titlebar">
    <!-- The Explorer address bar: ONE pill holds the whole thing. Home and the view
         toggle are permanent caps at either end; the middle is the path, and
         clicking it turns that stretch into a text field. So the bar never changes
         size or shape — only what fills its middle changes. -->
    <div class="search-inner">
      <!-- Every child of the pill is a direct sibling, so ONE `gap` spaces the lot
           and no element needs a margin of its own. (They were split across two
           containers before, which meant three competing spacing rules and a
           different gap depending on which pair you looked at.)

           mousedown.prevent on the buttons: blur fires BEFORE click, so without it
           clicking one while typing would collapse the field first and move the
           button out from under the pointer. -->
      <button class="bar-btn pill-btn" title="איפוס" @mousedown.prevent @click="goHome">
        <IconHome16Regular />
      </button>

      <div v-if="showSearch" class="pill-middle">
        <IconSearch20Regular class="search-icon" />
        <slot name="search" />
      </div>

      <!-- The path. Clicking anywhere that is not a crumb opens the field, the way
           an address bar does — the crumbs and chevrons stop their own clicks, so
           what reaches here is the slack around them. -->
      <div v-else class="pill-middle pill-path" @click="$emit('openSearch')">
        <BookCatalogBreadcrumb
          v-if="showBreadcrumb"
          :path="path"
          @navigate="$emit('navigate', $event)"
          @navigate-to-sibling="$emit('navigateToSibling', $event)"
        />
        <!-- Tree view has no path to walk; the stretch is just the way into search. -->
        <span v-else class="pill-hint">חיפוש</span>
      </div>

      <button
        v-if="!showSearch"
        class="bar-btn pill-btn"
        title="חיפוש"
        @mousedown.prevent
        @click="$emit('openSearch')"
      >
        <IconSearch20Regular />
      </button>

      <button class="bar-btn pill-btn" :title="viewToggleTitle" @mousedown.prevent @click="cycleView">
        <component :is="current.icon" :key="current.value" :class="{ 'rtl-flip': current.flip }" />
      </button>
    </div>
  </div>
</template>

<style scoped>
.titlebar {
  display: flex;
  align-items: center;
  /* No background of its own, so the chrome reads as one continuous surface from
     the app title bar down into the page — the separator below the bar is what
     divides it from the listing.

     Padding matches the full-text-search page's bar (TopSearchBar), so the pill
     sits at the same inset from the page edge on both pages. */
  padding: 8px 10px 6px;
  position: relative;
  z-index: 10;
}

/* ── The address bar ──────────────────────────────────────────────────────────
   Shape, fill and border come from the global `.search-inner` rule (main.css);
   padding, height and the inset shadow match the full-text-search page's pill
   (TopSearchBar), so the app's two search bars are one control.

   All spacing inside the pill is THIS ONE `gap`. Every child is a direct sibling
   of every other, so the buttons are evenly spaced by construction and none of
   them carries a margin — which is what stopped the ends from needing individual
   tuning every time one moved. Just enough to keep them apart; they are one
   cluster of controls, not separated groups. */
.search-inner {
  gap: 4px;
  padding: 0 12px;
  height: 30px;
  box-shadow: inset 0 1px 1px color-mix(in srgb, var(--text-primary) 6%, transparent);
  flex: 1 1 0;
  min-width: 0;
}

/* The pill's middle in both states — the field or the path — taking whatever the
   buttons leave. */
.pill-middle {
  display: flex;
  align-items: center;
  gap: 6px;
  flex: 1 1 0;
  min-width: 0;
  height: 100%;
}
/* The path stretch is clickable dead space — clicking it opens the field, so it
   takes a text caret rather than the pill's default arrow. */
.pill-path {
  cursor: text;
}
.search-inner :slotted(input) {
  flex: 1;
  /* Inputs carry an intrinsic min-width that would otherwise hold the pill open
     and push the buttons out of it. */
  min-width: 0;
  font-size: 13px;
  direction: rtl;
}
/* The magnifier is a hint for a field nobody is using yet; once focused it gives
   its width back to the text. `:focus-within` reads that straight off the DOM. */
.search-icon {
  flex-shrink: 0;
  width: 14px;
  height: 14px;
  color: var(--text-secondary);
}
.pill-middle:focus-within .search-icon {
  display: none;
}
/* Placeholder text for the states with no path to show (tree view). */
.pill-hint {
  flex: 1;
  min-width: 0;
  font-size: 13px;
  color: var(--text-secondary);
}

/* The pill's buttons take its scale, not the title bar's — the same rule the
   full-text-search bar's in-pill buttons follow. No margins: the pill's gap is
   the only thing that positions them. */
.pill-btn {
  width: 22px;
  height: 22px;
  padding: 0;
}
.rtl-flip {
  transform: scaleX(-1);
}
</style>
