<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useElementSize, useEventListener } from '@vueuse/core'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import HomeTile from './HomePageTile.vue'
import HomeSearchDropdown from './HomeSearchDropdown.vue'
import HomePageDateBar from './HomePageDateBar.vue'
import { useHomeSearch } from './useHomeSearch'
import { useHomeSearchBar } from './useHomeSearchBar'
import { useHomeSearchNavigation } from './useHomeSearchNavigation'
import { useHomeTiles } from './useHomeTiles'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useAppNavigation } from '@/composables/useAppNavigation'

const { navigate } = useAppNavigation()

const innerRef = ref<HTMLElement | null>(null)
const searchBarRef = ref<HTMLElement | null>(null)
const searchBarInputRef = ref<HTMLInputElement | null>(null)
const searchDropdownRef = ref<InstanceType<typeof HomeSearchDropdown> | null>(null)
const searchDropdownEl = computed(() => searchDropdownRef.value?.element ?? null)

const { width: containerWidth } = useElementSize(innerRef)

const {
  tiles,
  visibleRecentlyOpenedList,
  totalTileCount,
  getRecentTileIcon,
  onTogglePinRecent,
  onRemoveRecent,
} = useHomeTiles(containerWidth)

const homeSearchQuery = ref('')

const {
  catalogResults,
  catalogTocResults,
  hebrewBooksResults,
  fileResults,
  sourcePriority,
  isLoadingCatalogToc,
  isLoadingHebrewBooks,
  isLoadingFiles,
  hasAnyResults,
  isLoadingAny,
  clearResults,
  pause: pauseSearch,
  resume: resumeSearch,
} = useHomeSearch(homeSearchQuery)

// The search bar reports user intent (onSubmit / onDropdownKeydown) and this
// shell decides what it means, so the bar itself never imports navigation. Both
// cross-references are passed as arrow functions, which defers resolution to call
// time — neither declaration depends on the other's position.
const searchBar = useHomeSearchBar({
  query: homeSearchQuery,
  searchBarRef,
  hasAnyResults,
  isLoadingAny,
  clearResults,
  onSubmit: (query) => navigation.openFullTextSearch(query),
  // Combobox model: focus stays in the input; keydowns are forwarded to the
  // dropdown, which moves its highlight. Once the user is arrowing through
  // results, pause the async sources so late arrivals don't reshuffle the list
  // under the highlight — the next keystroke (onSearchInput) resumes them.
  onDropdownKeydown: (event) => {
    const handled = searchDropdownRef.value?.onSearchInputKeydown(event) ?? false
    if (handled) pauseSearch()
    return handled
  },
})

function onSearchInput() {
  resumeSearch()
  searchBar.onInput()
}

const navigation = useHomeSearchNavigation(() => searchBar.reset())

searchBar.openWhenAsyncResultsArrive([catalogTocResults, hebrewBooksResults, fileResults])

useDropdownClose(searchBarRef, searchBar.close, { ignore: [searchDropdownEl] })

function onTileKeydown(event: KeyboardEvent, index: number) {
  const isLastTile = index === totalTileCount.value - 1
  if (event.code === 'Tab' && !event.shiftKey && isLastTile) {
    event.preventDefault()
    searchBarInputRef.value?.focus()
  }
}

// On a cold start the hosting WebView2 may not hold OS focus yet when HomePage
// mounts — the host only gives the web content OS focus on NavigationCompleted /
// Form.Activated (see AppViewerFocus), which can land after us. focus() on an
// element whose window lacks OS focus silently doesn't stick, which is why the
// initial focus was unreliable on cold start but fine on later warm navigations.
// Fix: focus now if we already own OS focus, otherwise focus once the window
// gains it (the host's _webView.Focus() fires window's 'focus' event).
function focusSearchInput() {
  const input = searchBarInputRef.value
  if (!input) return
  if (document.hasFocus()) input.focus()
  else useEventListener(window, 'focus', () => input.focus(), { once: true })
}

onMounted(focusSearchInput)

async function onTap(label: string) {
  await navigate(label)
}
</script>

<template>
  <div class="home-page">
    <div ref="innerRef" class="home-inner">
      <div ref="searchBarRef" class="home-search-bar-wrapper">
        <div class="home-search-bar">
          <input
            ref="searchBarInputRef"
            v-model="homeSearchQuery"
            class="home-search-bar__field"
            type="search"
            :placeholder="searchBar.placeholder.value"
            autocomplete="off"
            @focus="searchBar.onFocus"
            @input="onSearchInput"
            @keydown="searchBar.onKeydown"
          />
          <button
            class="home-search-bar__search-button"
            tabindex="-1"
            title="הקלד לחיפוש שם ספר. לחץ כאן לחיפוש תוכן במאגרץ"
            @click="searchBar.submit"
          >
            <IconSearch20Regular />
          </button>
        </div>
        <HomeSearchDropdown
          v-if="searchBar.isDropdownOpen.value"
          ref="searchDropdownRef"
          :catalog-results="catalogResults"
          :catalog-toc-results="catalogTocResults"
          :hebrew-books-results="hebrewBooksResults"
          :file-results="fileResults"
          :source-priority="sourcePriority"
          :is-loading-catalog-toc="isLoadingCatalogToc"
          :is-loading-hebrew-books="isLoadingHebrewBooks"
          :is-loading-files="isLoadingFiles"
          :anchor-top="searchBar.anchorTop.value"
          :anchor-left="searchBar.anchorLeft.value"
          :anchor-right="searchBar.anchorRight.value"
          :max-height="searchBar.maxHeight.value"
          @select-catalog-book="navigation.onSelectCatalogBook"
          @select-catalog-toc="navigation.onSelectCatalogToc"
          @select-hebrew-book="navigation.onSelectHebrewBook"
          @select-file="navigation.onSelectFile"
        />
      </div>
      <div class="home-grid">
        <HomeTile
          v-for="(tile, index) in tiles"
          :key="tile.label"
          v-bind="tile"
          @tap="onTap(tile.label)"
          @keydown="onTileKeydown($event, index)"
        />
        <HomeTile
          v-for="(entry, index) in visibleRecentlyOpenedList"
          :key="entry.key"
          :label="entry.title"
          :icon="getRecentTileIcon(entry).icon"
          :color="getRecentTileIcon(entry).color"
          :pinned="entry.pinned"
          actions
          @tap="navigation.openRecentEntry(entry, $event)"
          @keydown="onTileKeydown($event, tiles.length + index)"
          @toggle-pin="onTogglePinRecent(entry)"
          @remove="onRemoveRecent(entry)"
        />
      </div>
    </div>

    <HomePageDateBar />
  </div>
</template>

<style scoped>
.home-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  outline: none;
  position: relative;
  container-type: inline-size;
}

/* Search + tiles scroll together as one group, centered when there's room and
   pushed toward the top once they overflow. Only the date bar stays fixed. */
.home-inner {
  display: flex;
  flex-direction: column;
  align-items: center;
  /* safe center: vertically centered when there's room, but aligns to the top
     once content overflows so the search bar stays reachable by scrolling. */
  justify-content: safe center;
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  /* No top padding: the sticky search wrapper supplies its own top spacing so
     it can stick flush to the scroll-area top with nothing peeking above it. */
  padding: 0 24px 16px;
}

.home-grid {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 4px 8px;
  max-width: 920px;
}

/* Search bar — flows with the tiles (centered as a group when there's room),
   but sticks to the top of the scroll area once the tiles scroll under it. */
.home-search-bar-wrapper {
  display: block;
  position: sticky;
  top: 0;
  z-index: 2;
  width: 100%;
  max-width: 560px;
  margin-bottom: 20px;
  padding: 16px 0 8px;
  flex-shrink: 0;
  /* Opaque backdrop so tiles scrolling underneath don't show through around
     the rounded search pill. */
  background: var(--bg-primary);
}

.home-search-bar {
  display: flex;
  align-items: center;
  gap: 6px;
  width: 100%;
  padding: 5px 10px;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 999px;
  transition: background 150ms;
  overflow: hidden;
  min-width: 0;
}

.home-search-bar:focus-within {
  background: var(--input-bg-focus);
}

.home-search-bar__field {
  flex: 1;
  min-width: 0;
  height: 100%;
  background: none;
  border: none;
  outline: none;
  font-size: 13px;
  color: var(--text-primary);
  direction: rtl;
}

.home-search-bar__field::placeholder {
  color: var(--text-secondary);
  opacity: 0.7;
}

.home-search-bar__field::-webkit-search-cancel-button {
  filter: grayscale(1) opacity(0.4);
}

.home-search-bar__search-button {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  padding: 0;
  border-radius: 4px;
  color: var(--text-secondary);
  opacity: 0.6;
}

.home-search-bar__search-button:hover {
  opacity: 1;
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
</style>
