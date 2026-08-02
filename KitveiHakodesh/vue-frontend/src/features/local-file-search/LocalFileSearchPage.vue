<script setup lang="ts">
import { ref, watch, onMounted, onBeforeUnmount, nextTick } from 'vue'
import { useIntervalFn } from '@vueuse/core'
import {
  IconSearch20Regular,
  IconWarning20Regular,
  IconArrowSort20Regular,
  IconCheckmark20Regular,
} from '@iconify-prerendered/vue-fluent'
import { storeToRefs } from 'pinia'
import BottomSearchBar from '@/components/BottomSearchBar.vue'
import LoadingAnimation from '@/components/LoadingAnimation.vue'
import LocalFileSearchResultsList from './LocalFileSearchResultsList.vue'
import { useLocalFileSearch } from './useLocalFileSearch'
import { addinDisplayTitle } from './otzariaAddins'
import type { LocalFileSearchSortOrder, LocalFileSearchResult } from './useLocalFileSearch'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { restoreLocalFile } from '@/webview-host/bridge'
import { useSettingsStore } from '@/stores/settingsStore'
import { useTabStore } from '@/stores/tabStore'

const paneNavigation = usePaneNavigation()
const tabStore = useTabStore()

// Capture this tab's id at mount time (singleton — stable for component lifetime)
const fileSearchTabId = paneNavigation.activeTabId

const searchQuery = ref('')
const settingsStore = useSettingsStore()
const { fileSearchSortOrder: sortOrder } = storeToRefs(settingsStore)

const {
  results, searching, showLoadingAnimation,
  totalCount, errorMessage,
} = useLocalFileSearch(searchQuery, sortOrder)

const searchInputElement = ref<HTMLInputElement | null>(null)
const resultsListElement = ref<InstanceType<typeof LocalFileSearchResultsList> | null>(null)
const openingFile = ref(false)

// ── Sort dropdown ─────────────────────────────────────────────────────────────

const isSortDropdownOpen = ref(false)
const sortToggleButtonElement = ref<HTMLElement | null>(null)
const sortControlElement = ref<HTMLElement | null>(null)

const { justClosed } = useDropdownClose(
  sortControlElement,
  () => { isSortDropdownOpen.value = false },
  { toggleButton: sortToggleButtonElement },
)

function toggleSortDropdown() {
  if (justClosed.value) return
  isSortDropdownOpen.value = !isSortDropdownOpen.value
}

const SORT_OPTIONS: { value: LocalFileSearchSortOrder; label: string }[] = [
  { value: 'relevance', label: 'ללא מיון' },
  { value: 'fileName',  label: 'שם קובץ' },
  { value: 'fileType',  label: 'סוג קובץ' },
  { value: 'fullPath',  label: 'נתיב מלא' },
  { value: 'date',      label: 'תאריך (חדש ראשון)' },
]

function selectSortOrder(value: LocalFileSearchSortOrder) {
  sortOrder.value = value
  isSortDropdownOpen.value = false
}

// ── Animated placeholder ──────────────────────────────────────────────────────
// Same typewriter treatment the full-text search bar uses, so the two search
// pages advertise their query syntax the same way.

const PLACEHOLDERS = [
  'חפש קבצים לפי שם או לפי תיקייה...',
  'הקלד תוספים: עבור תוספי אוצריא',
]
const placeholder = ref(PLACEHOLDERS[0]!)
let phraseIdx = 0, charIdx = 0, pauseTicks = 0

const { pause: pauseTyping, resume: resumeTyping } = useIntervalFn(() => {
  if (pauseTicks > 0) { pauseTicks--; return }
  const target = PLACEHOLDERS[phraseIdx]!
  if (charIdx < target.length) {
    placeholder.value = target.slice(0, ++charIdx)
  } else {
    pauseTicks = 12
    phraseIdx = (phraseIdx + 1) % PLACEHOLDERS.length
    charIdx = 0
  }
}, 80)

watch(searchQuery, (v) => (v ? pauseTyping() : resumeTyping()))

// ── Query persistence across navigation ───────────────────────────────────────

// Restore from the tab object on mount (singleton route — mounted once)
onMounted(() => {
  const savedQuery = paneNavigation.activeTab.fileSearchQuery
  if (savedQuery) {
    searchQuery.value = savedQuery
    pauseTyping() // the watch above only fires on change, not on this initial set
  }
  nextTick(() => searchInputElement.value?.focus())
})

// The page unmounts on every switch away. Two cases:
//  • Tab SWITCH — the tab is still a file-search tab, so persist the query and
//    restore it when the user switches back.
//  • In-place NAVIGATION (opened a file in this tab) — the tab's route has
//    already changed to a viewer, so the query is stale and must be cleared, not
//    saved. Opening in a NEW tab (Ctrl+click) leaves this tab a file-search tab
//    with its query intact, which is the whole point of that path.
onBeforeUnmount(() => {
  const tab = tabStore.tabs.find((t) => t.id === fileSearchTabId)
  const stillFileSearch = tab?.route === '/file-search'
  tabStore.updateTab(fileSearchTabId, {
    fileSearchQuery: stillFileSearch ? searchQuery.value || undefined : undefined,
  })
})

function focusResults() {
  resultsListElement.value?.focusContainer()
}

// No isHosted guard: it is TRUE in dev so it never gated anything, and opening a local file
// works in both modes (hosted serves it from its virtual host, dev from the service's
// capability-gated /khs-file proxy — restoreLocalFile picks the path).
async function onOpenFile(item: LocalFileSearchResult, openInNewTab = false) {
  openingFile.value = true

  try {
    const extension = item.fileName.substring(item.fileName.lastIndexOf('.')).toLowerCase()
    const dotIndex = item.fileName.lastIndexOf('.')
    const titleWithoutExtension = dotIndex > 0 ? item.fileName.substring(0, dotIndex) : item.fileName

    const isHtmlLike = extension === '.htm' || extension === '.html'
    const route = extension === '.txt' ? '/txt-view' : isHtmlLike ? '/html-view' : '/pdf-view'

    // The addin name is known synchronously, so the placeholder tab gets it too —
    // otherwise a Ctrl/⌘-clicked addin reads "index" until restoreLocalFile returns.
    const displayTitle = item.addinName ? addinDisplayTitle(item.addinName) : titleWithoutExtension

    // For a Ctrl/⌘-click, open a fresh tab up front and patch it by id. The PDF/
    // HTML path awaits restoreLocalFile, during which the active tab may change,
    // so we must never rely on "the active tab" after the await — always target
    // the captured id. .txt has no async step but takes the same path for parity.
    const targetTabId = openInNewTab
      ? paneNavigation.openTab({ route, title: displayTitle }).id
      : fileSearchTabId

    if (extension === '.txt') {
      tabStore.updateTab(targetTabId, {
        route: '/txt-view',
        title: displayTitle,
        localFileName: item.fileName,
        localFilePath: item.fullPath,
        localFileVirtualUrl: undefined,
        isOtzariaAddin: false,
      })
      return
    }

    const restored = await restoreLocalFile(item.fullPath)
    if (!restored?.url) return

    // Route by what is actually served: dev Word docs may come back as HTML
    // (Office-free fallback) — res.kind reports it. Fall back to the extension.
    const servedRoute =
      restored.kind === 'html' ? '/html-view' : restored.kind === 'pdf' ? '/pdf-view' : route

    // isOtzariaAddin is written unconditionally (updateTab merges, so omitting the
    // key would leave a previous addin's `true` on this tab) and gated on the
    // served route, since only /html-view ever reads it.
    tabStore.updateTab(targetTabId, {
      route: servedRoute as '/html-view' | '/pdf-view',
      title: displayTitle,
      localFileName: item.fileName,
      localFilePath: item.fullPath,
      localFileVirtualUrl: restored.url,
      isOtzariaAddin: !!item.addinName && servedRoute === '/html-view',
    })
  } finally {
    openingFile.value = false
  }
}
</script>

<template>
  <div class="local-file-search-page">
    <div class="local-file-search-content">
      <!-- Error state -->
      <div v-if="errorMessage" class="state-banner error-banner">
        <IconWarning20Regular class="banner-icon banner-icon--error" />
        <span>{{ errorMessage }}</span>
      </div>

      <!-- Results or empty state -->
      <div v-else class="results-container">
        <!-- Loading while any search is in flight for more than 200ms -->
        <div v-if="showLoadingAnimation" class="searching-state">
          <LoadingAnimation text="מחפש..." />
        </div>

        <!-- Empty state when idle with no results -->
        <div v-else-if="!results.length" class="empty-state">
          <IconSearch20Regular class="empty-icon" />
          <span class="empty-msg">{{ searchQuery.trim() ? 'לא נמצאו תוצאות' : 'חפש קבצים...' }}</span>
        </div>

        <!-- Results list -->
        <LocalFileSearchResultsList
          v-else
          ref="resultsListElement"
          :items="results"
          :searching="searching"
          @open-file="onOpenFile"
        />

        <!-- Opening overlay -->
        <div v-if="openingFile" class="opening-overlay">
          <div class="opening-card">
            <div class="opening-spinner" />
            <span class="opening-label">פותח קובץ…</span>
          </div>
        </div>

        <!-- Truncation notice -->
        <div v-if="totalCount > results.length" class="truncation-notice">
          (מוצגים {{ results.length }} מתוך {{ totalCount }} תוצאות)
        </div>
      </div>
    </div>

    <BottomSearchBar>
      <template #left>
        <!-- Sort toggle button -->
        <div ref="sortControlElement" class="sort-control">
          <button
            ref="sortToggleButtonElement"
            class="sort-toggle-button"
            :class="{ 'is-active': sortOrder !== 'relevance' }"
            :title="'מיון: ' + SORT_OPTIONS.find((o) => o.value === sortOrder)!.label"
            @click="toggleSortDropdown"
          >
            <IconArrowSort20Regular />
          </button>

          <!-- Sort dropdown -->
          <div v-if="isSortDropdownOpen" class="sort-dropdown">
            <div
              v-for="option in SORT_OPTIONS"
              :key="option.value"
              role="option"
              class="sort-dropdown__item"
              :class="{ 'is-selected': sortOrder === option.value }"
              @click="selectSortOrder(option.value)"
            >
              <IconCheckmark20Regular
                class="sort-dropdown__checkmark"
                :class="{ 'is-visible': sortOrder === option.value }"
              />
              <span>{{ option.label }}</span>
            </div>
          </div>
        </div>
      </template>
      <input
        ref="searchInputElement"
        v-model="searchQuery"
        type="search"
        class="search-input"
        :placeholder="placeholder"
        spellcheck="false"
        autocomplete="off"
        @keydown.up.prevent="focusResults"
        @keydown.down.prevent="focusResults"
        @keydown.tab.prevent="focusResults"
      />
      <template #right>
        <IconSearch20Regular class="search-icon" />
      </template>
    </BottomSearchBar>
  </div>
</template>

<style scoped>
.local-file-search-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--bg-primary);
}
.local-file-search-content {
  flex: 1;
  overflow: hidden;
  position: relative;
  display: flex;
  flex-direction: column;
}
.results-container {
  flex: 1;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  position: relative;
}
.search-icon {
  color: var(--text-secondary);
}
.search-input {
  flex: 1;
  background: none;
  border: none;
  outline: none;
  font-size: 13px;
  color: var(--text-primary);
}
.search-input::placeholder {
  color: var(--text-secondary);
}
.search-input::-webkit-search-cancel-button {
  filter: grayscale(1) opacity(0.4);
}

/* Banners */
.state-banner {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 14px;
  font-size: 12px;
  color: var(--text-secondary);
  background: color-mix(in srgb, var(--text-secondary) 8%, transparent);
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
}
.error-banner {
  color: var(--status-danger);
  background: color-mix(in srgb, var(--status-danger) 8%, transparent);
}
.banner-icon {
  flex-shrink: 0;
  font-size: 16px;
  color: inherit;
}
.banner-icon svg {
  color: inherit;
}

/* Searching state */
.searching-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  flex: 1;
}

/* Empty state */
.empty-state {
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
}
.empty-icon {
  width: 56px;
  height: 56px;
  opacity: 0.25;
  font-size: 56px;
}
.empty-msg {
  font-size: 14px;
  color: var(--text-secondary);
  opacity: 0.25;
  font-weight: 500;
}

/* Opening overlay */
.opening-overlay {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  background: color-mix(in srgb, var(--bg-primary) 80%, transparent);
  backdrop-filter: blur(2px);
  z-index: 10;
}
.opening-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  padding: 28px 40px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 12px;
}
.opening-spinner {
  width: 24px;
  height: 24px;
  border: 2px solid var(--border-color);
  border-top-color: var(--text-secondary);
  border-radius: 50%;
  animation: opening-spin 0.7s linear infinite;
}
@keyframes opening-spin {
  to { transform: rotate(360deg); }
}
.opening-label {
  font-size: 13px;
  color: var(--text-secondary);
}

/* Truncation notice */
.truncation-notice {
  padding: 4px 12px;
  font-size: 11px;
  color: var(--text-secondary);
  background: var(--bg-secondary);
  border-top: 1px solid var(--border-color);
  text-align: center;
  flex-shrink: 0;
}

/* Sort control */
.sort-control {
  position: relative;
  display: flex;
  align-items: center;
}

.sort-toggle-button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  color: var(--text-secondary);
  border-radius: 4px;
  padding: 0;
}

.sort-toggle-button.is-active {
  color: var(--accent-color, #0078d4);
}

.sort-dropdown {
  position: absolute;
  bottom: calc(100% + 6px);
  right: 0;
  min-width: 160px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.2);
  overflow: hidden;
  z-index: 100;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

.sort-dropdown__item {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 26px;
  padding: 0 10px;
  font-size: 12px;
  color: var(--text-primary);
  cursor: pointer;
  white-space: nowrap;
}

.sort-dropdown__item:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}

.sort-dropdown__item:active {
  background: color-mix(in srgb, var(--text-primary) 10%, transparent);
}

.sort-dropdown__item.is-selected {
  color: var(--accent-color, #0078d4);
}

.sort-dropdown__checkmark {
  flex-shrink: 0;
  opacity: 0;
  color: var(--accent-color, #0078d4);
}

.sort-dropdown__checkmark.is-visible {
  opacity: 1;
}
</style>
