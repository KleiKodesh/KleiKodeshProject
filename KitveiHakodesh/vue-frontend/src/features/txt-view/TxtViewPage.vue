<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, watch, nextTick, inject } from 'vue'
import { useTabStore } from '@/stores/tabStore'
import { useLocalFileStore } from '@/stores/localFileStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { readTxtFileContent } from '@/webview-host/bridge'
import { isHosted } from '@/webview-host/seforimDb'
import { useZoomHandler, ZOOM_CONFIG } from '@/composables/useZoom'
import { useTxtViewSearch } from './useTxtViewSearch'
import { useTxtViewCopyMenu, useTxtViewScopedCopy } from './useTxtViewCopyMenu'
import { removeDiacriticsForSearch, stripHtmlForSearch } from '@/utils/hebrewTextProcessing'
import { useUiChromeVisibility } from '@/composables/useUiChromeVisibility'
import { onLongPress } from '@vueuse/core'
import { getTheme } from '@/theme/themes'
import type { ThemePreset } from '@/theme/themeTypes'
import ContextMenu from '@/components/ContextMenu.vue'
import {
  IconChevronUp20Regular,
  IconChevronDown20Regular,
  IconDismiss20Regular,
} from '@iconify-prerendered/vue-fluent'

const tabStore = useTabStore()
const localFileStore = useLocalFileStore()
const settingsStore = useSettingsStore()
const bookViewStore = useBookViewStore()
const paneId = inject<1 | 2>('paneId', 1)
const paneNavigation = usePaneNavigation()

const tabId = paneNavigation.activeTabId
const filePath = computed(() => paneNavigation.activeTab.localFilePath ?? null)
const virtualUrl = computed(() => paneNavigation.activeTab.localFileVirtualUrl ?? null)
const htmlMaskEnabled = computed(() => settingsStore.pdfPageFilters)

const { titleBarVisible } = useUiChromeVisibility()
const APP_TITLE_BAR_HEIGHT = 40
const searchBarStyle = computed(() => ({
  top: `${(titleBarVisible.value ? APP_TITLE_BAR_HEIGHT : 0) + 4}px`,
}))

const rawContent = ref<string | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)

const scrollContainerRef = ref<HTMLDivElement | null>(null)
const contextMenuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
let scrollSaveTimer: number | null = null

// ── Context menu ──────────────────────────────────────────────────────────────

const { items: contextMenuItems, buildFormattedHtml } = useTxtViewCopyMenu({
  scrollerEl: scrollContainerRef,
})
useTxtViewScopedCopy(scrollContainerRef, buildFormattedHtml)

onLongPress(scrollContainerRef, (event) => {
  contextMenuRef.value?.showAtPosition(event.clientX, event.clientY)
})

// ── Zoom ──────────────────────────────────────────────────────────────────────

const zoom = ref<number>(ZOOM_CONFIG.DEFAULT)
const isTxtViewActive = computed(() => paneNavigation.activeTab.route === '/txt-view')

useZoomHandler({ zoom, enabled: isTxtViewActive })

const fontPx = computed(() => (zoom.value / 100) * (settingsStore.fontSize / 100) * 15)

async function saveZoom() {
  const existing = (await tabStore.getTabViewState(tabId)) ?? {}
  tabStore.setTabViewState(tabId, {
    ...existing,
    txtViewZoom: zoom.value !== ZOOM_CONFIG.DEFAULT ? zoom.value : undefined,
  })
}

watch(zoom, saveZoom)

// ── Line parsing ──────────────────────────────────────────────────────────────

interface ParsedLine {
  tag: 'h2' | 'div'
  html: string
  rawText: string // diacritics-stripped for search matching
}

const parsedLines = computed((): ParsedLine[] => {
  if (!rawContent.value) return []
  return rawContent.value.split(/\r\n|\r|\n/).flatMap((line): ParsedLine[] => {
    const stripped = line.replace(/&nbsp;/gi, '').trim()
    if (!stripped) return []
    const first = line.charAt(0)
    if (first === '@' || first === '#' || first === '$') {
      const headerText = line.substring(1).trim()
      return [{ tag: 'h2', html: headerText, rawText: stripHtmlForSearch(headerText) }]
    }
    if (first === '!') {
      const content = line.substring(1)
      return [{ tag: 'div', html: content, rawText: stripHtmlForSearch(content) }]
    }
    return [{ tag: 'div', html: line, rawText: stripHtmlForSearch(line) }]
  })
})

// ── Search ────────────────────────────────────────────────────────────────────

const searchVisible = ref(false)
const searchInputRef = ref<HTMLInputElement | null>(null)

const {
  query: searchQuery,
  matchCount,
  currentMatchIndex,
  currentMatchLineIndex,
  currentMatchOccurrence,
  next: searchNext,
  previous: searchPrevious,
  clear: searchClear,
} = useTxtViewSearch(
  () => parsedLines.value.map((l) => l.rawText),
  () => {
    // Return the index of the first line currently visible in the scroll container
    if (!scrollContainerRef.value) return 0
    const container = scrollContainerRef.value
    const children = container.children
    const containerTop = container.getBoundingClientRect().top
    for (let i = 0; i < children.length; i++) {
      const childBottom = children[i]!.getBoundingClientRect().bottom
      if (childBottom > containerTop) return i
    }
    return 0
  },
)

function openSearch() {
  searchVisible.value = true
  bookViewStore.txtViewSearchVisible = true
  nextTick(() => searchInputRef.value?.focus())
}

function closeSearch() {
  searchVisible.value = false
  bookViewStore.txtViewSearchVisible = false
  searchClear()
}

// Watch the toggle signal fired from the title bar button or Ctrl+F in AppTitleBar
watch(
  () => bookViewStore.txtViewToggleSearchSignal,
  (signal) => {
    if (signal.paneId !== paneId) return
    if (searchVisible.value) closeSearch()
    else openSearch()
  },
)

// Keep store in sync when the component is torn down (tab switch / close)
onBeforeUnmount(() => {
  bookViewStore.txtViewSearchVisible = false
})

function scrollToCurrentMatch() {
  const lineIndex = currentMatchLineIndex.value
  if (lineIndex < 0 || !scrollContainerRef.value) return
  const container = scrollContainerRef.value
  const element = container.children[lineIndex] as HTMLElement | undefined
  if (!element) return

  // Let the browser do the minimum scroll to make it visible
  element.scrollIntoView({ behavior: 'instant', block: 'nearest' })

  // Then check if the element is hidden behind the search bar and nudge down
  const APP_TITLE_BAR_HEIGHT = 40
  const SEARCH_BAR_HEIGHT = 36
  const topClearance = (titleBarVisible.value ? APP_TITLE_BAR_HEIGHT : 0) + SEARCH_BAR_HEIGHT + 8

  const containerRect = container.getBoundingClientRect()
  const elementTop = element.getBoundingClientRect().top - containerRect.top
  if (elementTop < topClearance) {
    container.scrollTop -= topClearance - elementTop
  }
}

function navigateNext() {
  searchNext()
  nextTick(scrollToCurrentMatch)
}

function navigatePrevious() {
  searchPrevious()
  nextTick(scrollToCurrentMatch)
}

function onSearchKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter') event.shiftKey ? navigatePrevious() : navigateNext()
  else if (event.key === 'Escape') closeSearch()
}

// Remove auto-scroll watch — only scroll on explicit next/prev navigation

// Highlight the matching query substring within the line html.
// Walks the html string tag-aware, inserts <mark> only around the specific
// occurrence that matches — each occurrence in the line is a separate search result.
function highlightedHtml(line: ParsedLine, lineIndex: number): string {
  if (!searchQuery.value) return line.html

  const normalizedQuery = removeDiacriticsForSearch(searchQuery.value)
  if (!normalizedQuery || !line.rawText.includes(normalizedQuery)) return line.html

  // Walk html tag-aware, building a map from text-position → html-position
  const html = line.html
  const textPositions: number[] = [] // textPositions[i] = index in html string for text char i
  let insideTag = false
  for (let i = 0; i < html.length; i++) {
    if (html[i] === '<') { insideTag = true; continue }
    if (html[i] === '>') { insideTag = false; continue }
    if (!insideTag) textPositions.push(i)
  }

  // Find all occurrence start positions in the normalized text
  const normalizedText = line.rawText // already stripHtmlForSearch-processed
  const occurrenceStarts: number[] = []
  let pos = 0
  while ((pos = normalizedText.indexOf(normalizedQuery, pos)) !== -1) {
    occurrenceStarts.push(pos)
    pos++
  }

  if (!occurrenceStarts.length) return html

  // Determine which occurrences to highlight on this line
  // All occurrences get the regular mark; only the current match gets --current
  const isCurrentLine = lineIndex === currentMatchLineIndex.value
  const currentOccurrenceIndex = isCurrentLine ? currentMatchOccurrence.value : -1

  // Build result by inserting <mark> tags around each occurrence
  // occurrenceStarts[k] is a text-char index; map to html positions via textPositions
  type Segment = { htmlStart: number; htmlEnd: number; isCurrent: boolean }
  const segments: Segment[] = []
  for (let k = 0; k < occurrenceStarts.length; k++) {
    const textStart = occurrenceStarts[k]!
    const textEnd = textStart + normalizedQuery.length - 1
    if (textStart >= textPositions.length) continue
    const htmlStart = textPositions[textStart]!
    const htmlEnd = textEnd < textPositions.length
      ? textPositions[textEnd]! + 1
      : textPositions[textPositions.length - 1]! + 1
    segments.push({ htmlStart, htmlEnd, isCurrent: k === currentOccurrenceIndex })
  }

  // Assemble the final string
  let result = ''
  let cursor = 0
  for (const seg of segments) {
    result += html.slice(cursor, seg.htmlStart)
    const markClass = seg.isCurrent ? 'search-match current' : 'search-match'
    result += `<mark class="${markClass}">${html.slice(seg.htmlStart, seg.htmlEnd)}</mark>`
    cursor = seg.htmlEnd
  }
  result += html.slice(cursor)
  return result
}

const matchLabel = computed(() => {
  if (!searchQuery.value) return ''
  if (matchCount.value === 0) return 'לא נמצא'
  return `${currentMatchIndex.value + 1} / ${matchCount.value}`
})

// ── Content loading ───────────────────────────────────────────────────────────

async function loadContent() {
  loading.value = true
  error.value = null
  rawContent.value = null

  try {
    if (isHosted && filePath.value) {
      const content = await readTxtFileContent(filePath.value)
      if (content === null) {
        error.value = 'הקובץ לא נמצא'
      } else {
        rawContent.value = content
      }
    } else if (virtualUrl.value) {
      const response = await fetch(virtualUrl.value)
      if (!response.ok) {
        error.value = 'שגיאה בטעינת הקובץ'
      } else {
        rawContent.value = await response.text()
      }
    } else {
      error.value = 'אין קובץ פתוח'
    }
  } catch {
    error.value = 'שגיאה בטעינת הקובץ'
  } finally {
    loading.value = false
  }
}

watch([filePath, virtualUrl], loadContent)

onMounted(async () => {
  // Restore zoom before content loads so font size is correct on first render
  const saved = await tabStore.getTabViewState(tabId)
  if (saved?.txtViewZoom != null) zoom.value = saved.txtViewZoom
  await loadContent()
  await restoreScrollPosition()
})

// ── Scroll persistence ────────────────────────────────────────────────────────

function onScroll() {
  const scrollTop = scrollContainerRef.value?.scrollTop ?? 0
  if (scrollSaveTimer !== null) clearTimeout(scrollSaveTimer)
  scrollSaveTimer = window.setTimeout(() => saveScrollPosition(scrollTop), 400)
}

async function saveScrollPosition(scrollTop: number) {
  scrollSaveTimer = null
  const existing = (await tabStore.getTabViewState(tabId)) ?? {}
  tabStore.setTabViewState(tabId, { ...existing, htmlViewScrollTop: scrollTop })
}

async function restoreScrollPosition() {
  const state = await tabStore.getTabViewState(tabId)
  if (!state?.htmlViewScrollTop || !scrollContainerRef.value) return
  scrollContainerRef.value.scrollTop = state.htmlViewScrollTop
}

onBeforeUnmount(() => {
  if (scrollSaveTimer !== null) {
    clearTimeout(scrollSaveTimer)
    scrollSaveTimer = null
  }
})

// ── Theme filter ──────────────────────────────────────────────────────────────

const htmlFilter = computed(() => {
  if (!htmlMaskEnabled.value) return 'none'
  const preset = document.documentElement.getAttribute('data-theme-preset') as ThemePreset | null
  const theme = preset ? getTheme(preset) : null
  if (!theme) return 'invert(0.85) hue-rotate(180deg) sepia(0.2)'
  return 'invert(0.85) hue-rotate(180deg) sepia(0.2) brightness(0.95) contrast(0.95)'
})

async function retry() {
  await loadContent()
}
</script>

<template>
  <div class="txt-view-page" :style="{ filter: htmlFilter }">
    <ContextMenu ref="contextMenuRef" :items="contextMenuItems" />
    <div v-if="loading" class="txt-state-message">
      <span>בטעינה...</span>
    </div>
    <div v-else-if="error" class="txt-state-message txt-state-message--error">
      <span>{{ error }}</span>
      <button @click="retry">נסה שוב</button>
    </div>
    <div
      v-else
      ref="scrollContainerRef"
      class="txt-content"
      :style="{ fontSize: `${fontPx}px` }"
      @scroll="onScroll"
      @contextmenu="contextMenuRef?.show($event)"
    >
      <template v-for="(line, index) in parsedLines" :key="index">
        <h2 v-if="line.tag === 'h2'" dir="auto" v-html="highlightedHtml(line, index)" />
        <div v-else class="txt-line" dir="auto" v-html="highlightedHtml(line, index)" />
      </template>
    </div>

    <!-- Search bar — same visual style as BookViewSearchBar -->
    <Transition name="search-bar">
      <div v-if="searchVisible" class="search-bar" :style="searchBarStyle">
        <div class="search-inner">
          <input
            ref="searchInputRef"
            v-model="searchQuery"
            type="search"
            class="search-input"
            placeholder="חיפוש בטקסט..."
            spellcheck="true"
            autocomplete="on"
            @keydown="onSearchKeydown"
          />
          <span class="match-count" :class="{ 'no-match': searchQuery && matchCount === 0 }">
            {{ matchLabel }}
          </span>
        </div>
        <button class="nav-btn" :disabled="matchCount === 0" @click="navigatePrevious">
          <IconChevronUp20Regular />
        </button>
        <button class="nav-btn" :disabled="matchCount === 0" @click="navigateNext">
          <IconChevronDown20Regular />
        </button>
        <span class="sep" />
        <button class="close-btn" @click="closeSearch"><IconDismiss20Regular /></button>
      </div>
    </Transition>
  </div>
</template>

<style scoped>
.txt-view-page {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.txt-content {
  flex: 1;
  overflow-y: auto;
  padding: 7.5px 16px;
  direction: rtl;
  text-align: justify;
  font-family: var(--text-font);
  line-height: var(--line-height, 1.7);
  color: var(--text-primary);
  background: var(--bg-primary);
  white-space: pre-wrap;
  word-break: break-word;
  scrollbar-color: var(--border-color) transparent;
}

.txt-content h2 {
  font-family: var(--header-font);
  font-size: 18px;
  font-weight: 600;
  margin: 16px 0 8px 0;
  white-space: normal;
}

.txt-content > * {
  content-visibility: auto;
}

.txt-state-message {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 10px;
  color: var(--text-secondary);
  font-size: 14px;
}

.txt-state-message--error {
  color: var(--text-primary);
}

/* ── Search bar ────────────────────────────────────────────────────────────── */

.search-bar {
  position: fixed;
  z-index: 9999;
  left: 0;
  right: 0;
  margin: 0 auto;
  display: flex;
  align-items: center;
  gap: 2px;
  width: fit-content;
  padding: 1px 3px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.4), 0 1px 3px rgba(0, 0, 0, 0.25);
}

.search-inner {
  display: flex;
  align-items: center;
  padding: 1px 6px;
  gap: 4px;
}

.search-input {
  width: 130px;
  border: none;
  background: none;
  outline: none;
  font-size: 13px;
  color: var(--text-primary);
  cursor: text;
  direction: rtl;
}

.search-input::placeholder { color: var(--text-secondary); }
.search-input::-webkit-search-cancel-button { filter: grayscale(1) opacity(0.4); }

.match-count {
  font-size: 11px;
  color: var(--text-secondary);
  white-space: nowrap;
  flex-shrink: 0;
  min-width: 32px;
  text-align: end;
}

.match-count.no-match { color: #e05252; }

.sep {
  width: 1px;
  height: 16px;
  background: var(--border-color);
  flex-shrink: 0;
  margin-inline: 1px;
}

.nav-btn,
.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  flex-shrink: 0;
  border-radius: 4px;
  cursor: pointer;
}

.nav-btn svg, .close-btn svg { width: 16px; height: 16px; }
.nav-btn:disabled { opacity: 0.3; cursor: default; }

.search-bar-enter-active,
.search-bar-leave-active {
  transition: opacity 150ms ease, transform 150ms ease;
}

.search-bar-enter-from,
.search-bar-leave-to {
  opacity: 0;
  transform: translateY(-6px);
}

/* ── Search match highlighting ─────────────────────────────────────────────── */

:deep(.search-match) {
  background: rgba(255, 165, 0, 0.4);
  color: inherit;
  border-radius: 2px;
}

:deep(.search-match.current) {
  background: rgba(255, 165, 0, 0.9);
  color: #000;
}
</style>
