<script setup lang="ts">
import { useBookCatalogCategoryNavigation } from '@/features/book-catalog/useBookCatalogCategoryNavigation'
import { ref, computed, onMounted } from 'vue'
import { useEventListener } from '@vueuse/core'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import HomeSuggestionCard from './HomeSuggestionCard.vue'
import GlobalSearchDropdown from '@/features/global-search/GlobalSearchDropdown.vue'
import HomePageDateBar from './HomePageDateBar.vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { useGlobalSearch } from '@/features/global-search/useGlobalSearch'
import { useHomeSearchBar } from './useHomeSearchBar'
import { useHomeSearchNavigation } from './useHomeSearchNavigation'
import { useHomeSuggestionCards } from './useHomeSuggestionCards'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useAppNavigation } from '@/composables/useAppNavigation'

const settingsStore = useSettingsStore()

const { navigate, openFolderPicker } = useAppNavigation()
const { openCatalogCategory } = useBookCatalogCategoryNavigation()

const searchBarRef = ref<HTMLElement | null>(null)
const searchBarInputRef = ref<HTMLInputElement | null>(null)
const searchDropdownRef = ref<InstanceType<typeof GlobalSearchDropdown> | null>(null)
const searchDropdownEl = computed(() => searchDropdownRef.value?.element ?? null)


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
  hasIndexFailureToShow,
  isLoadingAny,
  clearResults,
  pause: pauseSearch,
  resume: resumeSearch,
} = useGlobalSearch(homeSearchQuery)

// The search bar reports user intent (onSubmit / onDropdownKeydown) and this
// shell decides what it means, so the bar itself never imports navigation. Both
// cross-references are passed as arrow functions, which defers resolution to call
// time — neither declaration depends on the other's position.
const searchBar = useHomeSearchBar({
  query: homeSearchQuery,
  searchBarRef,
  hasAnyResults: () => hasAnyResults() || hasIndexFailureToShow(),
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

useDropdownClose(searchBarRef, searchBar.close, { ignore: [searchDropdownEl], fitToViewport: false })

// History, today's learning and the destinations — one collection, one card style.
const cards = useHomeSuggestionCards(
  {
    openRecent: (entry, openInNewTab) => navigation.openRecentEntry(entry, openInNewTab),
    openFolder: (path, openInNewTab) => openFolderPicker(path, openInNewTab),
    openCatalogCategory,
  },
  navigate,
)

// Tab off the last card wraps back to the search bar.
function onCardKeydown(event: KeyboardEvent, index: number) {
  const isLastCard = index === cards.value.length - 1
  if (event.code === 'Tab' && !event.shiftKey && isLastCard) {
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

</script>

<template>
  <div class="home-page">
    <div class="home-inner">
      <div class="home-search-blanket">
        <div
          ref="searchBarRef"
          class="home-search-bar-wrapper"
          >
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
              class="bar-btn home-search-bar__search-button"
              tabindex="-1"
              :title="$t('home.searchButtonTitle')"
              @click="searchBar.submit"
            >
              <IconSearch20Regular />
            </button>
          </div>
          <GlobalSearchDropdown
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
      </div>
      <div class="home-cards">
        <HomeSuggestionCard
          v-for="(card, index) in cards"
          :key="card.key"
          :card="card"
          @keydown="onCardKeydown($event, index)"
        />
      </div>
    </div>

    <!-- Pinned, App.vue renders it under every page instead. -->
    <HomePageDateBar v-if="!settingsStore.dateBarPinned" />
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
  /* Shared width cap of the search bar and the card collection. */
  --home-content-width: 720px;
}

/* Search + cards scroll together as one group. Only the date bar stays fixed.

   Centered or top-anchored is decided purely by WIDTH (see the container query
   below), never by how tall the content is. Narrow is the default: a narrow pane
   needs every row it can get, so the group starts at the top. */
.home-inner {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: flex-start;
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  /* No top padding: the sticky search wrapper supplies its own top spacing so
     it can stick flush to the scroll-area top with nothing peeking above it. */
  padding: 0 clamp(12px, 4cqi, 24px) 16px;
}

/* Wide pane: center the group vertically.

   `safe center`, NOT plain `center`. This is a scroll container (overflow-y:
   auto) in column direction, and plain `center` on one of those splits NEGATIVE
   free space between both ends when the content overflows: the first child's top
   edge goes above the scroll origin, which no scroll offset can reach, so the
   search bar ends up clipped and unscrollable in a short pane. `safe` falls back
   to start-alignment in exactly that case.

   `safe` used to make this read as top-anchored even on a wide screen, but that
   was the 45-tile budget overflowing almost every pane. With the flat card cap
   (useHomeHistoryCards) the content fits, so `safe` centers as intended and only yields in
   the short-pane case where centering cannot be rendered anyway.

   A container query, not a media query, so a split pane is judged on ITS OWN
   width — half a wide monitor is a narrow pane and gets the top layout. */
@container (min-width: 480px) {
  .home-inner {
    justify-content: safe center;
  }
}

.home-cards {
  display: grid;
  /* Card min-width scales with the pane (cqi), so narrow panes still fit 2+
     columns instead of collapsing to one full-width card per row. */
  grid-template-columns: repeat(auto-fill, minmax(min(100%, clamp(140px, 30cqi, 220px)), 1fr));
  gap: 8px;
  width: 100%;
  max-width: var(--home-content-width);
}

/* Search bar — flows with the cards (centered as a group when there's room),
   but sticks to the top of the scroll area once the cards scroll under it.
   The blanket spans the scroll area's full width with an opaque page-colored
   backdrop, so cards scrolling underneath truly disappear instead of peeking
   out around the sides of the search pill. */
.home-search-blanket {
  position: sticky;
  top: 0;
  z-index: 2;
  display: flex;
  justify-content: center;
  width: 100%;
  flex-shrink: 0;
  margin-bottom: 8px;
  background: var(--bg-primary);
}

/* Same width cap as the card collection beneath it. */
.home-search-bar-wrapper {
  display: block;
  width: 100%;
  max-width: var(--home-content-width);
  /* Trimmed with the pill, so the height it saves is not handed straight back as
     whitespace around it. */
  padding: 12px 0 6px;
}

/* The same 30px pill the catalog, dictionary and every other TopSearchBar page
   uses — components/TopSearchBar.vue sets that height explicitly so every page
   agrees, and this page is no exception. The 13px field font already matches, so
   height and padding are all that differ. Keep the two in step: if TopSearchBar's
   height changes, change it here too. */
.home-search-bar {
  display: flex;
  align-items: center;
  gap: 6px;
  width: 100%;
  height: 30px;
  /* Same insets and inset shadow as TopSearchBar, so the button sits where the FTS
     bar's does. */
  padding-inline: 12px;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 999px;
  box-shadow: inset 0 1px 1px color-mix(in srgb, var(--text-primary) 6%, transparent);
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
}

.home-search-bar__field::placeholder {
  color: var(--text-secondary);
  opacity: 0.7;
}

.home-search-bar__field::-webkit-search-cancel-button {
  filter: grayscale(1) opacity(0.4);
}

/* Box, hover and press come from the global .bar-btn rule — same size as the
   FullTextSearchBar's search button. */
.home-search-bar__search-button {
  flex-shrink: 0;
  width: 20px;
  height: 20px;
  padding: 0;
}
</style>
