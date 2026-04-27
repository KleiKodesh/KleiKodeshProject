<script setup lang="ts">
import { ref, watch, nextTick, onMounted, onUnmounted } from 'vue'
import {
  IconBookmark20Regular,
  IconSearch20Regular,
  IconChevronUp20Regular,
  IconChevronDown20Regular,
  IconZoomIn20Regular,
  IconZoomOut20Regular,
  IconMoreHorizontal20Regular,
  IconArrowDownload20Regular,
  IconCrop20Regular,
  IconDismiss20Regular,
} from '@iconify-prerendered/vue-fluent'

const props = defineProps<{ iframeEl: HTMLIFrameElement | null }>()

// ── State ─────────────────────────────────────────────────────────────────────

const currentPage = ref(1)
const totalPages = ref(0)
const currentZoom = ref('auto')
const moreOpen = ref(false)
const findOpen = ref(false)
const pageInputValue = ref('1')

// Find state
const findQuery = ref('')
const findMatchCount = ref<number | null>(null)
const findMatchIndex = ref<number | null>(null)
const findNotFound = ref(false)
const findInputRef = ref<HTMLInputElement | null>(null)

const ZOOM_OPTIONS = [
  { value: 'auto', label: 'אוטומטי' },
  { value: 'page-fit', label: 'התאם עמוד' },
  { value: 'page-width', label: 'רוחב עמוד' },
  { value: '0.5', label: '50%' },
  { value: '0.75', label: '75%' },
  { value: '1', label: '100%' },
  { value: '1.25', label: '125%' },
  { value: '1.5', label: '150%' },
  { value: '2', label: '200%' },
]

// ── PDF.js access ─────────────────────────────────────────────────────────────

function getPdfApp(): any {
  return (props.iframeEl?.contentWindow as any)?.PDFViewerApplication ?? null
}

// ── State sync ────────────────────────────────────────────────────────────────

let syncInterval: ReturnType<typeof setInterval> | null = null

function syncState() {
  const app = getPdfApp()
  if (!app?.pdfViewer) return
  const page = app.pdfViewer.currentPageNumber
  const total = app.pdfViewer.pagesCount
  const scale = app.pdfViewer.currentScaleValue
  if (page) {
    currentPage.value = page
    pageInputValue.value = String(page)
  }
  if (total) totalPages.value = total
  if (scale) currentZoom.value = scale
}

// Listen for PDF.js find results to update match count
function attachFindListener() {
  const app = getPdfApp()
  if (!app?.eventBus) return
  app.eventBus.on('updatefindmatchescount', (data: any) => {
    findMatchCount.value = data.matchesCount?.total ?? null
    findMatchIndex.value = data.matchesCount?.current ?? null
    findNotFound.value = false
  })
  app.eventBus.on('updatefindcontrolstate', (data: any) => {
    // state 1 = NOT_FOUND
    if (data.state === 1) {
      findNotFound.value = true
      findMatchCount.value = null
      findMatchIndex.value = null
    } else {
      findNotFound.value = false
    }
  })
}

onMounted(() => {
  syncInterval = setInterval(syncState, 400)
  // Attach find listeners once the iframe is ready — retry until app is available
  const tryAttach = setInterval(() => {
    if (getPdfApp()?.eventBus) {
      attachFindListener()
      clearInterval(tryAttach)
    }
  }, 300)
})

onUnmounted(() => {
  if (syncInterval) clearInterval(syncInterval)
})

// ── Page / zoom controls ──────────────────────────────────────────────────────

function prevPage() {
  getPdfApp()?.pdfViewer?.previousPage()
}

function nextPage() {
  getPdfApp()?.pdfViewer?.nextPage()
}

function zoomIn() {
  getPdfApp()?.zoomIn()
}

function zoomOut() {
  getPdfApp()?.zoomOut()
}

function setZoom(value: string) {
  const app = getPdfApp()
  if (!app) return
  app.pdfViewer.currentScaleValue = value
  currentZoom.value = value
}

function commitPageInput() {
  const app = getPdfApp()
  if (!app) return
  const page = parseInt(pageInputValue.value, 10)
  if (!isNaN(page) && page >= 1 && page <= totalPages.value) {
    app.pdfViewer.currentPageNumber = page
    currentPage.value = page
  } else {
    pageInputValue.value = String(currentPage.value)
  }
}

function zoomLabel(value: string): string {
  const option = ZOOM_OPTIONS.find((o) => o.value === value)
  if (option) return option.label
  const numeric = parseFloat(value)
  if (!isNaN(numeric)) return Math.round(numeric * 100) + '%'
  return value
}

// ── Sidebar ───────────────────────────────────────────────────────────────────

function toggleSidebar() {
  const btn = props.iframeEl?.contentDocument?.getElementById('viewsManagerToggleButton')
  btn?.click()
}

// ── Find ──────────────────────────────────────────────────────────────────────

function dispatchFind(type: string, findPrevious = false) {
  const app = getPdfApp()
  if (!app?.eventBus) return
  app.eventBus.dispatch('find', {
    source: window,
    type,
    query: findQuery.value,
    caseSensitive: false,
    entireWord: false,
    highlightAll: true,
    findPrevious,
    matchDiacritics: true,
  })
}

function openFind() {
  findOpen.value = true
  findNotFound.value = false
  findMatchCount.value = null
  findMatchIndex.value = null
  nextTick(() => findInputRef.value?.focus())
}

function closeFind() {
  findOpen.value = false
  findQuery.value = ''
  findNotFound.value = false
  findMatchCount.value = null
  findMatchIndex.value = null
  // Clear highlights
  const app = getPdfApp()
  app?.eventBus?.dispatch('find', {
    source: window,
    type: '',
    query: '',
    caseSensitive: false,
    entireWord: false,
    highlightAll: false,
    findPrevious: false,
    matchDiacritics: false,
  })
}

function toggleFind() {
  if (findOpen.value) {
    closeFind()
  } else {
    openFind()
  }
}

watch(findQuery, () => {
  if (!findOpen.value) return
  if (findQuery.value === '') {
    findMatchCount.value = null
    findMatchIndex.value = null
    findNotFound.value = false
  }
  dispatchFind('')
})

function findNext() {
  dispatchFind('again', false)
}

function findPrev() {
  dispatchFind('again', true)
}

function onFindKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter') {
    event.shiftKey ? findPrev() : findNext()
  } else if (event.key === 'Escape') {
    closeFind()
  }
}

// ── More menu ─────────────────────────────────────────────────────────────────

function download() {
  getPdfApp()?.downloadOrSave()
  moreOpen.value = false
}

function rectangleSelect() {
  const win = props.iframeEl?.contentWindow as any
  win?.toggleRectangleSelection?.()
  moreOpen.value = false
}
</script>

<template>
  <div class="pdf-toolbar-wrap" dir="rtl">
    <!-- Main toolbar row -->
    <div class="pdf-toolbar">
      <!-- Sidebar toggle -->
      <button class="tool-btn" title="תוכן עניינים" @click="toggleSidebar">
        <IconBookmark20Regular />
      </button>

      <div class="separator" />

      <!-- Find toggle -->
      <button class="tool-btn" :class="{ active: findOpen }" title="חיפוש" @click="toggleFind">
        <IconSearch20Regular />
      </button>

      <div class="separator" />

      <!-- Page navigation -->
      <button class="tool-btn" title="עמוד קודם" @click="prevPage">
        <IconChevronUp20Regular />
      </button>

      <div class="page-input-wrap">
        <input
          v-model="pageInputValue"
          class="page-input"
          type="number"
          min="1"
          :max="totalPages"
          @change="commitPageInput"
          @keydown.enter="commitPageInput"
        />
        <span v-if="totalPages" class="page-total">/ {{ totalPages }}</span>
      </div>

      <button class="tool-btn" title="עמוד הבא" @click="nextPage">
        <IconChevronDown20Regular />
      </button>

      <div class="separator" />

      <!-- Zoom -->
      <button class="tool-btn" title="הקטן" @click="zoomOut">
        <IconZoomOut20Regular />
      </button>

      <select
        class="zoom-select"
        :value="currentZoom"
        @change="setZoom(($event.target as HTMLSelectElement).value)"
      >
        <option v-for="option in ZOOM_OPTIONS" :key="option.value" :value="option.value">
          {{ option.label }}
        </option>
        <option v-if="!ZOOM_OPTIONS.find((o) => o.value === currentZoom)" :value="currentZoom">
          {{ zoomLabel(currentZoom) }}
        </option>
      </select>

      <button class="tool-btn" title="הגדל" @click="zoomIn">
        <IconZoomIn20Regular />
      </button>

      <div class="separator" />

      <!-- More menu -->
      <div class="more-wrap">
        <button
          class="tool-btn"
          :class="{ active: moreOpen }"
          title="עוד"
          @click="moreOpen = !moreOpen"
        >
          <IconMoreHorizontal20Regular />
        </button>
        <div v-if="moreOpen" class="more-menu">
          <button class="more-item" @click="download">
            <IconArrowDownload20Regular />
            <span>הורדה</span>
          </button>
          <button class="more-item" @click="rectangleSelect">
            <IconCrop20Regular />
            <span>בחירת אזור</span>
          </button>
          <button class="more-item" @click="moreOpen = false">
            <IconDismiss20Regular />
            <span>סגור</span>
          </button>
        </div>
      </div>
    </div>

    <!-- Find bar — floating pill, same style as BookViewSearchBar -->
    <Transition name="search-bar">
      <div v-if="findOpen" class="search-bar">
        <div class="search-inner">
          <input
            ref="findInputRef"
            v-model="findQuery"
            type="search"
            class="search-input"
            placeholder="חיפוש..."
            @keydown="onFindKeydown"
          />
          <span class="match-count" :class="{ 'no-match': findNotFound }">
            {{ findNotFound ? 'לא נמצא' : findMatchCount !== null ? `${findMatchIndex} / ${findMatchCount}` : '' }}
          </span>
        </div>
        <span class="sep" />
        <button class="nav-btn" @click="findPrev"><IconChevronUp20Regular /></button>
        <button class="nav-btn" @click="findNext"><IconChevronDown20Regular /></button>
        <span class="sep" />
        <button class="close-btn" @click="closeFind"><IconDismiss20Regular /></button>
      </div>
    </Transition>
  </div>
</template>

<style scoped>
.pdf-toolbar-wrap {
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
  background: var(--bg-secondary);
  border-bottom: 1px solid var(--border-color);
}

/* ── Main toolbar ── */
.pdf-toolbar {
  display: flex;
  align-items: center;
  height: 44px;
  padding: 0 4px;
  gap: 2px;
}

.tool-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  border-radius: 4px;
  flex-shrink: 0;
  color: var(--text-secondary);
}

.tool-btn svg {
  width: 18px;
  height: 18px;
}

.tool-btn.active {
  color: var(--accent-color);
  background: color-mix(in srgb, var(--accent-color) 12%, transparent);
}

.separator {
  width: 1px;
  height: 20px;
  background: var(--border-color);
  margin: 0 2px;
  flex-shrink: 0;
}

.page-input-wrap {
  display: flex;
  align-items: center;
  gap: 3px;
  flex-shrink: 0;
}

.page-input {
  width: 36px;
  height: 28px;
  text-align: center;
  font-size: 12px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: var(--bg-primary);
  color: var(--text-primary);
  padding: 0;
  -moz-appearance: textfield;
}

.page-input::-webkit-outer-spin-button,
.page-input::-webkit-inner-spin-button {
  -webkit-appearance: none;
}

.page-total {
  font-size: 11px;
  color: var(--text-secondary);
  white-space: nowrap;
}

.zoom-select {
  height: 28px;
  font-size: 11px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: var(--bg-primary);
  color: var(--text-primary);
  padding: 0 4px;
  flex-shrink: 0;
  max-width: 80px;
  cursor: pointer;
}

.more-wrap {
  position: relative;
  flex-shrink: 0;
}

.more-menu {
  position: absolute;
  top: calc(100% + 4px);
  left: 0;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.2);
  z-index: 100;
  min-width: 130px;
  overflow: hidden;
}

.more-item {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  height: 36px;
  padding: 0 12px;
  font-size: 13px;
  color: var(--text-primary);
  text-align: right;
  border-radius: 0;
}

.more-item svg {
  width: 16px;
  height: 16px;
  color: var(--text-secondary);
  flex-shrink: 0;
}

.more-item:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}

/* ── Find bar ── */
.find-bar {
  display: flex;
  align-items: center;
  height: 44px;
  padding: 0 6px;
  gap: 4px;
  border-top: 1px solid var(--border-color);
}

.find-input-pill {
  flex: 1;
  display: flex;
  align-items: center;
  gap: 6px;
  height: 32px;
  padding: 0 10px;
  border: 1px solid var(--border-color);
  border-radius: 999px;
  background: var(--bg-primary);
  min-width: 0;
}

.find-icon {
  width: 14px;
  height: 14px;
  color: var(--text-secondary);
  flex-shrink: 0;
}

.find-input {
  flex: 1;
  font-size: 13px;
  color: var(--text-primary);
  background: none;
  border: none;
  outline: none;
  min-width: 0;
  direction: rtl;
}

.find-input::placeholder {
  color: var(--text-secondary);
}

.find-status {
  font-size: 11px;
  color: var(--text-secondary);
  white-space: nowrap;
  flex-shrink: 0;
}

.find-status.not-found {
  color: #e05252;
}

.find-nav-btn {
  width: 32px;
  height: 32px;
  flex-shrink: 0;
}

.find-close-btn {
  width: 32px;
  height: 32px;
  flex-shrink: 0;
}
</style>
