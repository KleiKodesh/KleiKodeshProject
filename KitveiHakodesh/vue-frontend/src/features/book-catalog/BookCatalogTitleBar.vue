<script setup lang="ts">
import { IconHome16Regular, IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import TopSearchBar from '@/components/TopSearchBar.vue'
import BookCatalogBreadcrumb from './BookCatalogBreadcrumb.vue'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'

defineProps<{
  path: CategoryNode[]
  /** Which face the bar is showing: the search field, or the path. */
  showSearch: boolean
}>()
defineEmits<{
  navigate: [number]
  navigateToSibling: [{ atIndex: number; node: CategoryNode }]
  openSearch: []
  goHome: []
}>()
</script>

<template>
  <!-- The Explorer address bar: ONE pill holds the whole thing. Home sits at one
       end and the search button at the other; the middle is the path, and clicking
       it turns that stretch into a text field. The buttons are in the bar's own end
       slots, so they hold their positions when the middle swaps — switching faces
       cannot resize or reflow the bar.

       It IS the app's search bar (TopSearchBar), not a copy of it: the padding,
       height, pill and inset shadow come from there, which is what keeps this and
       the full-text-search bar the same control. `gap` is tightened because this
       one has a button at each end rather than an input filling it. -->
  <TopSearchBar gap="4px">
    <!-- mousedown.prevent on every button: blur fires BEFORE click, so without it
         clicking one while typing would collapse the field first and move the
         button out from under the pointer. -->
    <template #left>
      <button class="bar-btn pill-btn" title="איפוס" @mousedown.prevent @click="$emit('goHome')">
        <IconHome16Regular />
      </button>
    </template>

    <!-- v-show, NOT v-if: the field carries the page's keydown handler, and the
         whole page is one combobox driven from it — the arrows move a highlight
         through the list below whether or not the user is searching. Unmounting it
         with the path face took the keyboard with it, so browsing by keyboard died
         the moment you entered a folder. It stays mounted and keeps focus; only
         its visibility changes. -->
    <div v-show="showSearch" class="pill-middle">
      <IconSearch20Regular class="search-icon" />
      <slot name="search" />
    </div>

    <!-- The path. Clicking anywhere that is not a crumb opens the field, the way
         an address bar does — the crumbs and chevrons stop their own clicks, so
         what reaches here is the slack around them. -->
    <div v-show="!showSearch" class="pill-middle pill-path" @click="$emit('openSearch')">
      <BookCatalogBreadcrumb
        :path="path"
        @navigate="$emit('navigate', $event)"
        @navigate-to-sibling="$emit('navigateToSibling', $event)"
      />
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
/* The pill's buttons take its scale, not the title bar's — the same 20px the
   full-text-search bar gives its own in-pill buttons, both sitting in a 30px
   pill. No margins: the bar's gap is the only thing that positions them. */
.pill-btn {
  width: 20px;
  height: 20px;
  padding: 0;
}
</style>
