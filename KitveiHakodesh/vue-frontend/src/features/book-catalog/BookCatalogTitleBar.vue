<script setup lang="ts">
import { computed } from 'vue'
import {
  IconTextBulletList20Regular,
  IconGrid20Regular,
  IconTextBulletListTree20Regular,
  IconHome16Regular,
  IconSearch20Regular,
} from '@iconify-prerendered/vue-fluent'
import TopSearchBar from '@/components/TopSearchBar.vue'
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
  <!-- The Explorer address bar: ONE pill holds the whole thing. Home sits at one
       end and the search/view buttons at the other; the middle is the path, and
       clicking it turns that stretch into a text field. The buttons are in the
       bar's own end slots, so they hold their positions when the middle swaps —
       switching faces cannot resize or reflow the bar.

       It IS the app's search bar (TopSearchBar), not a copy of it: the padding,
       height, pill and inset shadow come from there, which is what keeps this and
       the full-text-search bar the same control. `gap` is tightened because this
       one has a button at each end rather than an input filling it. -->
  <TopSearchBar gap="4px">
    <!-- mousedown.prevent on every button: blur fires BEFORE click, so without it
         clicking one while typing would collapse the field first and move the
         button out from under the pointer. -->
    <template #left>
      <button class="bar-btn pill-btn" title="איפוס" @mousedown.prevent @click="goHome">
        <IconHome16Regular />
      </button>
    </template>

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

    <template #right>
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
    </template>
  </TopSearchBar>
</template>

<style scoped>
/* The pill itself — padding, height, fill, border, inset shadow, and the slotted
   input's type size — all come from TopSearchBar. What is left here is only what
   this bar adds: the swapping middle, and the buttons in its end slots.

   The middle takes whatever the end buttons leave. `flex-basis: 0` keeps its width
   independent of what is typed into it, so a long query cannot widen the field and
   push the buttons out. */
.pill-middle {
  display: flex;
  align-items: center;
  gap: 6px;
  flex: 1 1 0;
  min-width: 0;
  height: 100%;
}
/* The path stretch is clickable dead space — clicking it opens the field, so it
   takes a text caret rather than the default arrow. */
.pill-path {
  cursor: text;
}
:deep(.search-inner input) {
  flex: 1;
  /* Inputs carry an intrinsic min-width that would otherwise hold the pill open
     and push the buttons out of it. */
  min-width: 0;
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
/* The pill's buttons take its scale, not the title bar's — the same 20px the
   full-text-search bar gives its own in-pill buttons, both sitting in a 30px
   pill. No margins: the bar's gap is the only thing that positions them. */
.pill-btn {
  width: 20px;
  height: 20px;
  padding: 0;
}
.rtl-flip {
  transform: scaleX(-1);
}
</style>
