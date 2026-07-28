<script setup lang="ts">
import { ref, computed, defineAsyncComponent } from 'vue'
import { useWindowSize } from '@vueuse/core'
import { useUiChromeVisibility } from '@/composables/useUiChromeVisibility'
import { useAppShellPane } from '@/composables/useAppShellPane'
import {
  IconLineHorizontal320Regular,
  IconAdd20Regular,
  IconDismiss20Regular,
  IconHome20Regular,
  IconOptions24Regular,
  IconOptions24Filled,
  IconColor24Regular,
  IconColor24Filled,
  IconConvertToText24Regular,
  IconSearch24Regular,
  IconSplitVertical20Regular,
  IconSplitVertical20Filled,
} from '@iconify-prerendered/vue-fluent'
import ThemeToggle from '@/theme/ThemeToggle.vue'
// The dropdown is v-if — lazy-load it so its imports (including fluent-color icons)
// don't add to the cold-start parse cost. It loads on first open, which is imperceptible.
const AppTitleBarNavDropdown = defineAsyncComponent(() => import('./AppTitleBarNavDropdown.vue'))
const AddressBar = defineAsyncComponent(() => import('./AddressBar.vue'))
import AppTitleBarTocBreadcrumb from './AppTitleBarTocBreadcrumb.vue'
import AppTitleBarBreadcrumbChevronDropdown from './AppTitleBarBreadcrumbChevronDropdown.vue'
import { useAppTitleBarTocBreadcrumb } from './useAppTitleBarTocBreadcrumb'
import { useAppTitleBarShortcuts } from './useAppTitleBarShortcuts'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { usePdfOcrStore } from '@/stores/pdfOcrStore'
import { isVstoEnvironment as isVsto } from '@/webview-host/bridge'

const props = withDefaults(defineProps<{ paneId?: 1 | 2 }>(), { paneId: 1 })

const pane = useAppShellPane(props.paneId)
const bookViewStore = useBookViewStore()
const settingsStore = useSettingsStore()
const pdfOcrStore = usePdfOcrStore()
const { titleBarVisible } = useUiChromeVisibility(props.paneId)

const { width: windowWidth } = useWindowSize()

// Split view requires enough horizontal space for two usable panes.
const SPLIT_VIEW_MIN_WIDTH = 768
const isSplitViewAvailable = computed(() => !isVsto && windowWidth.value >= SPLIT_VIEW_MIN_WIDTH)

// ── TOC breadcrumb ────────────────────────────────────────────────────────────

const {
  segments: tocBreadcrumbSegments,
  rootTocEntries: tocBreadcrumbRootTocEntries,
  rootPdfEntries: tocBreadcrumbRootPdfEntries,
} = useAppTitleBarTocBreadcrumb(
  () => activeTab.value?.route,
  () => activeTab.value?.tocPath,
  () => pane.activeTabId.value,
  (tabId) => bookViewStore.getTocBridge(tabId),
  (tabId) => bookViewStore.getPdfBridge(tabId),
)

function onNavigateToBreadcrumbEntry(entry: import('@/features/book-view/toc/useBookViewToc').TocEntry) {
  bookViewStore.getTocBridge(pane.activeTabId.value)?.navigateToEntry(entry)
}

function onNavigateToPdfBreadcrumbEntry(entry: import('@/stores/bookViewStore').PdfOutlineEntry) {
  bookViewStore.getPdfBridge(pane.activeTabId.value)?.navigateToEntry(entry)
}

// ── Button visibility helpers ─────────────────────────────────────────────────

function isTitleBarButtonVisible(buttonId: string): boolean {
  return !settingsStore.titleBarHiddenButtons.includes(buttonId)
}

const activeTab = computed(() => pane.activeTab.value)
const navDropdownOpen = ref(false)
const barRef = ref<HTMLElement | null>(null)
const navBtnRef = ref<HTMLElement | null>(null)

const isPdfTab = computed(
  () => activeTab.value?.route === '/pdf-view' || activeTab.value?.route === '/html-view',
)

// bookViewStore.isBookViewActive and isTxtViewActive read from tabStore.activeTab (pane 1).
// For pane 2 we compute these directly from the pane's active tab.
const isBookViewActive = computed(() => activeTab.value?.route === '/book-view')
const isTxtViewActive = computed(() => activeTab.value?.route === '/txt-view')

// A click always enters search mode; the address-bar dropdown doubles as the
// tab list (shown while the field is empty / has no results).
const barTitleHint = 'לחץ לניווט מהיר ולרשימת הלשוניות (Ctrl+T)'

const barTitle = computed(() => {
  const full = activeTab.value?.tocPath
    ? activeTab.value.title + ' · ' + activeTab.value.tocPath
    : activeTab.value?.title
  return full ? full + '\n' + barTitleHint : barTitleHint
})

const toolbarTitle = computed(() => {
  const baseTitle = isBookViewActive.value
    ? bookViewStore.getToolbarVisible(props.paneId) ? 'הסתר סרגל כלים' : 'הצג סרגל כלים'
    : activeTab.value?.pdfViewerTitleBarVisible !== false ? 'הסתר סרגל כותרת PDF' : 'הצג סרגל כותרת PDF'
  return `${baseTitle} (Ctrl+B)`
})

const pdfFilterTitle = computed(() =>
  settingsStore.pdfPageFilters ? 'בטל החלת ערכת נושא על דפי PDF' : 'החל ערכת נושא על דפי PDF',
)

// ── Title-bar search (Explorer-style address bar) ─────────────────────────────
// The title becomes an editable search field, reusing the home-page search.
// A single click always enters search mode — the address bar's dropdown shows
// the pane's tab list while the field is empty (or has no results), so it
// replaces the old dedicated tab-list dropdown in every environment.
const searchMode = ref(false)

function enterSearchMode() {
  // The address bar lives inside the header — make sure it's visible first
  // (Ctrl+H may have hidden it).
  titleBarVisible.value = true
  searchMode.value = true
}

function onTitleBarClick() {
  if (searchMode.value) return
  enterSearchMode()
}

function toggleNavDropdown() {
  navDropdownOpen.value = !navDropdownOpen.value
}

/** Ctrl+T: the address bar's dropdown doubles as the tab list (empty field = tab list). */
function toggleAddressBar() {
  if (searchMode.value) searchMode.value = false
  else enterSearchMode()
}

useAppTitleBarShortcuts({
  paneId: props.paneId,
  pane,
  titleBarVisible,
  isSplitViewAvailable,
  toggleAddressBar,
  toggleNavDropdown,
})

</script>

<template>
  <!-- Keyboard event listener is always active (above), but only render the visual header when titleBarVisible is true -->
  <div ref="barRef" class="title-bar-container" :class="{ hidden: !titleBarVisible }">
    <header class="title-bar" @click="onTitleBarClick">
    <div class="bar-start">
      <div class="nav-btn-wrap">
        <button
          v-if="isTitleBarButtonVisible('hamburger')"
          ref="navBtnRef"
          class="bar-btn"
          tabindex="-1"
          title="תפריט (Ctrl+M)"
          @click.stop="toggleNavDropdown"
        >
          <IconLineHorizontal320Regular />
        </button>
      </div>
      <ThemeToggle v-if="isTitleBarButtonVisible('theme-toggle')" tabindex="-1" />
      <button
        v-if="isTxtViewActive"
        class="bar-btn"
        tabindex="-1"
        title="חיפוש בטקסט (Ctrl+F)"
        @click.stop="bookViewStore.txtViewToggleSearch(props.paneId)"
      >
        <IconSearch24Regular />
      </button>
      <button
        v-if="isTitleBarButtonVisible('pdf-filter') && isPdfTab"
        class="bar-btn"
        tabindex="-1"
        :title="pdfFilterTitle"
        @click.stop="settingsStore.togglePdfPageFilters()"
      >
        <IconColor24Filled v-if="settingsStore.pdfPageFilters" />
        <IconColor24Regular v-else />
      </button>
      <button
        v-if="isTitleBarButtonVisible('toolbar-toggle') && (isBookViewActive || activeTab?.route === '/pdf-view')"
        class="bar-btn"
        tabindex="-1"
        :title="toolbarTitle"
        @click.stop="isBookViewActive ? bookViewStore.toggleToolbar(props.paneId) : pane.togglePdfViewerTitleBar()"
      >
        <IconOptions24Filled v-if="isBookViewActive ? bookViewStore.getToolbarVisible(props.paneId) : activeTab?.pdfViewerTitleBarVisible !== false" />
        <IconOptions24Regular v-else />
      </button>
      <button
        v-if="isTitleBarButtonVisible('split-view') && isSplitViewAvailable"
        class="bar-btn"
        tabindex="-1"
        :title="bookViewStore.splitViewEnabled ? 'סגור תצוגה מפוצלת (Ctrl+|)' : 'פתח תצוגה מפוצלת (Ctrl+|)'"
        @click.stop="bookViewStore.toggleSplitView()"
      >
        <IconSplitVertical20Filled v-if="bookViewStore.splitViewEnabled" />
        <IconSplitVertical20Regular v-else />
      </button>
    </div>

    <!-- Search mode — the title turns into an editable address-bar search. -->
    <AddressBar
      v-if="searchMode"
      :pane-id="props.paneId"
      class="bar-search"
      @close="searchMode = false"
    />

    <span v-else class="bar-title" dir="rtl" :title="barTitle">
      <!-- Interactive breadcrumb for book-view and pdf-view tabs -->
      <AppTitleBarTocBreadcrumb
        v-if="tocBreadcrumbSegments.length > 0 || tocBreadcrumbRootTocEntries.length > 0 || tocBreadcrumbRootPdfEntries.length > 0"
        :book-title="activeTab?.title ?? ''"
        :segments="tocBreadcrumbSegments"
        :root-toc-entries="tocBreadcrumbRootTocEntries"
        :root-pdf-entries="tocBreadcrumbRootPdfEntries"
        @navigate-to-toc-entry="onNavigateToBreadcrumbEntry"
        @navigate-to-pdf-entry="onNavigateToPdfBreadcrumbEntry"
      />
      <!-- Plain title + toc path for all other routes -->
      <template v-else>
        <span class="bar-title-name">{{ activeTab?.title }}</span>
        <template v-if="activeTab?.tocPath">
          <template v-for="segment in activeTab.tocPath.split(' · ')" :key="segment">
            <AppTitleBarBreadcrumbChevronDropdown :siblings="[]" :active-sibling-id="null" />
            <span class="bar-toc-segment"><bdi>{{ segment }}</bdi></span>
          </template>
        </template>
      </template>
    </span>

    <div class="bar-end">
      <button
        v-if="isTitleBarButtonVisible('ocr') && activeTab?.route === '/pdf-view'"
        class="bar-btn"
        tabindex="-1"
        :class="{ active: pdfOcrStore.isActive }"
        title="בחירת טקסט באזור (OCR)"
        @click.stop="pdfOcrStore.toggle()"
      >
        <IconConvertToText24Regular />
      </button>
      <button v-if="isTitleBarButtonVisible('home')" class="bar-btn" tabindex="-1" title="בית (Ctrl+G)" @click.stop="pane.goHome()"><IconHome20Regular /></button>
      <button v-if="isTitleBarButtonVisible('new-tab')" class="bar-btn" tabindex="-1" title="לשונית חדשה (Ctrl+N)" @click.stop="pane.openNewTab()">
        <IconAdd20Regular />
      </button>
      <button
        v-if="isTitleBarButtonVisible('close-tab')"
        class="bar-btn"
        tabindex="-1"
        title="סגור לשונית (Ctrl+W)"
        @click.stop="pane.closeTab(pane.activeTabId.value)"
      >
        <IconDismiss20Regular />
      </button>
    </div>

  </header>

  <!-- Nav dropdown — kept outside header so it stays visible when header is hidden -->
  <AppTitleBarNavDropdown
    v-if="navDropdownOpen"
    :toggle-button-el="navBtnRef"
    @close="navDropdownOpen = false"
    @click.stop
  />
  </div>
</template>

<style scoped>
/* ── Title bar layout — Explorer address-bar model ────────────────────────────
 * Three-zone layout: left buttons | address-bar box | right buttons.
 *   - .bar-start / .bar-end are flex: 0 0 auto → each hugs its buttons.
 *   - .bar-title (and .bar-search in search mode) is flex: 1 1 auto → the
 *     bordered box FILLS all remaining width between the two button groups,
 *     like the Windows Explorer address bar. Not centered, not content-sized.
 *   - min-width: 0 + overflow: hidden on .bar-title lets it shrink below its
 *     natural content width so the inner text ellipsizes.
 *
 * The persistent border on .bar-title is intentional — it's the affordance that
 * signals the bar is a clickable input. Search mode swaps .bar-title for the
 * editable .bar-search (AddressBar) in the same footprint.
 * ──────────────────────────────────────────────────────────────────────────── */
.title-bar-container {
  position: relative;
}
.title-bar-container.hidden .title-bar {
  display: none;
}
.title-bar {
  display: flex;
  align-items: center;
  height: var(--title-bar-height);
  padding: var(--title-bar-padding);
  background: var(--bg-secondary);
  /* Divider width is driven by the settings store: 0 when the content border is
     on (seamless merge), 1px when off (title bar shows its own divider). */
  border-bottom: var(--title-bar-divider-width, 0px) solid var(--border-color);
  position: relative;
  /* Regular arrow over the bar and the breadcrumb/title; only the buttons and
     breadcrumb chevrons use the pointer (hand). */
  cursor: default;
}
.bar-start {
  display: flex;
  align-items: center;
  gap: 0;
  flex: 0 0 auto;
}
.nav-btn-wrap {
  position: relative;
}
.bar-end {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 0;
  flex: 0 0 auto;
}
/* The title/breadcrumb is styled as a Windows-Explorer address bar: a bordered
   box that FILLS the space between the button groups (not a centered, content-
   sized label). The persistent border is the affordance that tells the user the
   bar is a clickable input. Clicking it enters search mode (AddressBar), which
   reuses the very same box footprint (.bar-search) for a seamless swap. */
.bar-title {
  display: flex;
  align-items: center;
  /* Box fills the bar (Explorer address-bar), but the breadcrumb/title inside it
     is centered while inactive. Editing swaps in .bar-search, whose field is
     start-aligned for typing. */
  justify-content: center;
  flex: 1 1 auto;
  min-width: 0;
  overflow: hidden;
  height: 24px;
  margin-inline: 6px;
  padding-inline: 6px;
  font-weight: 400;
  font-size: 0.82rem;
  color: var(--text-secondary);
  white-space: nowrap;
  cursor: default;
  /* Blend into the title bar (--bg-secondary) rather than stand out as a filled
     field — a subtle, uniform 1px frame is enough of an input hint. All four
     sides match at rest; the accent underline appears only on focus. */
  background: color-mix(in srgb, var(--text-primary) 3%, transparent);
  border: 1px solid var(--border-color);
  border-radius: 6px;
}
.bar-title:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
/* Search mode swaps in the editable AddressBar, occupying the same box. */
.bar-search {
  flex: 1 1 auto;
  min-width: 0;
  margin-inline: 6px;
}
/* Block pointer events on text spans so clicks bubble to the header toggle,
   but leave buttons (chevrons) fully interactive. */
.bar-title .breadcrumb-title-name,
.bar-title .bar-title-name,
.bar-title .bar-toc-segment,
.bar-title .bar-toc-path,
.bar-title .breadcrumb-segment {
  pointer-events: none;
}
.bar-title-name {
  unicode-bidi: isolate;
  direction: ltr;
}
.bar-toc-path {
  color: var(--text-secondary);
  opacity: 0.7;
}
.bar-toc-segment {
  color: var(--text-secondary);
  opacity: 0.7;
  overflow: hidden;
  text-overflow: clip;
  white-space: nowrap;
  flex-shrink: 1;
  min-width: 0;
  margin-inline-end: 2px;
  /* Cut off the START of the segment, keeping the tail visible: LTR paragraph
     clips at the right edge (the RTL text's start); the inner <bdi> keeps
     natural Hebrew rendering. Same trick as .breadcrumb-segment — and, like
     there, intentionally the opposite of the title, which clips its END
     (see the truncation note in AppTitleBarTocBreadcrumb.vue). */
  direction: ltr;
}
.bar-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--title-bar-button-size);
  height: var(--title-bar-button-size);
  padding: 6px;
  border-radius: 4px;
}
.bar-btn svg {
  width: 16px;
  height: 16px;
}
.bar-btn.active {
  color: var(--accent-color);
  background: color-mix(in srgb, var(--accent-color) 15%, transparent);
  box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--accent-color) 30%, transparent);
}
</style>
