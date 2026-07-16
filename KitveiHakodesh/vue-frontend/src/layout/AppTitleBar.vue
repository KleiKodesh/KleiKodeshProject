<script setup lang="ts">
import { ref, computed, defineAsyncComponent } from 'vue'
import { useEventListener, useWindowSize } from '@vueuse/core'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useUiChromeVisibility } from '@/composables/useUiChromeVisibility'
import { useAppShellPane } from '@/composables/useAppShellPane'
import { useAppNavigation } from '@/composables/useAppNavigation'
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
// Both dropdowns are v-if — lazy-load them so their imports (including fluent-color icons)
// don't add to the cold-start parse cost. They load on first open, which is imperceptible.
const AppTitleBarTabDropdown = defineAsyncComponent(() => import('./AppTitleBarTabDropdown.vue'))
const AppTitleBarNavDropdown = defineAsyncComponent(() => import('./AppTitleBarNavDropdown.vue'))
const AddressBar = defineAsyncComponent(() => import('./AddressBar.vue'))
import AppTitleBarTocBreadcrumb from './AppTitleBarTocBreadcrumb.vue'
import AppTitleBarBreadcrumbChevronDropdown from './AppTitleBarBreadcrumbChevronDropdown.vue'
import { useAppTitleBarTocBreadcrumb } from './useAppTitleBarTocBreadcrumb'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { usePdfOcrStore } from '@/stores/pdfOcrStore'
import { useThemeStore } from '@/theme/themeStore'
import { toggleFullscreen, toggleChromeTabList, isVstoEnvironment as isVsto, hasNativeChromeTabs } from '@/webview-host/bridge'

const props = withDefaults(defineProps<{ paneId?: 1 | 2 }>(), { paneId: 1 })

const pane = useAppShellPane(props.paneId)
const bookViewStore = useBookViewStore()
const settingsStore = useSettingsStore()
const pdfOcrStore = usePdfOcrStore()
const themeStore = useThemeStore()
const { navigateInNewTab } = useAppNavigation()
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
const dropdownOpen = ref(false)
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

// Interaction hint depends on the environment:
//   - Demo/standalone (native chrome strip): single click = search;
//     Ctrl+T opens the (native) tab list.
//   - VSTO / dev browser (no strip): single click = tab list;
//     double click = search.
// Ctrl+E focuses the search everywhere.
// Each hint line is separated by a newline (tooltips render \n as line breaks).
const barTitleHint = computed(() =>
  hasNativeChromeTabs
    ? 'לחץ לחיפוש (Ctrl+E)\nCtrl+T לרשימת הלשוניות'
    : 'לחץ להצגת רשימת הלשוניות (Ctrl+T)\nלחיצה כפולה לחיפוש (Ctrl+E)',
)

const barTitle = computed(() => {
  const full = activeTab.value?.tocPath
    ? activeTab.value.title + ' · ' + activeTab.value.tocPath
    : activeTab.value?.title
  const hint = barTitleHint.value
  return full ? full + '\n' + hint : hint
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

const { justClosed } = useDropdownClose(barRef, () => {
  dropdownOpen.value = false
})

function toggleTabDropdown() {
  if (justClosed.value) return
  dropdownOpen.value = !dropdownOpen.value
}

// ── Title-bar search (Explorer-style address bar) ─────────────────────────────
// The title becomes an editable search field, reusing the home-page search.
// The gesture split depends on whether a native chrome tab strip is present:
//   - Native strip present (standalone/demo): the strip already lists the tabs,
//     so the Vue tab dropdown isn't used here — a single click on the Vue title
//     bar switches straight to search mode.
//   - No native strip (VSTO task pane AND the dev browser): single click opens
//     the Vue tab-list dropdown; double-click switches to search mode.
const searchMode = ref(false)

function enterSearchMode() {
  dropdownOpen.value = false
  // The address bar lives inside the header — make sure it's visible first
  // (Ctrl+H may have hidden it).
  titleBarVisible.value = true
  searchMode.value = true
}

function onTitleBarClick() {
  if (searchMode.value) return
  if (hasNativeChromeTabs) {
    // Standalone/demo: single click = search mode.
    enterSearchMode()
  } else {
    // VSTO / dev browser: single click = Vue tab list; search via double-click.
    toggleTabDropdown()
  }
}

function onTitleBarDblClick() {
  // Double-click always enters search mode (the primary gesture where a single
  // click opens the tab list, and a harmless equivalent to a single click where
  // it already enters search mode).
  if (!hasNativeChromeTabs) dropdownOpen.value = false
  enterSearchMode()
}

function toggleNavDropdown() {
  navDropdownOpen.value = !navDropdownOpen.value
  dropdownOpen.value = false
}

function selectTab(id: string) {
  pane.switchTab(id)
  dropdownOpen.value = false
}

// Keyboard shortcuts — each pane installs its own handler.
// Pane-scoped shortcuts (tab ops, book view actions, navigation within a pane)
// only fire when this pane is the focused pane.
// App-wide shortcuts (theme, fullscreen, split view, quick-nav, settings) are
// handled exclusively by pane 1 — they must not fire twice.

// Forward Ctrl+key shortcuts from child iframes (HTML viewer) back into the
// top-level keydown pipeline. Only pane 1 needs to do this — iframes only
// appear in pane 1 (txt-view / html-view).
if (props.paneId === 1) {
  useEventListener('message', (e: MessageEvent) => {
    if (!e.data || e.data.type !== 'iframeKeydown') return
    window.dispatchEvent(new KeyboardEvent('keydown', {
      code: e.data.code,
      ctrlKey: e.data.ctrlKey,
      shiftKey: e.data.shiftKey,
      metaKey: e.data.metaKey,
      altKey: e.data.altKey,
      bubbles: true,
      cancelable: true,
    }))
  })
}

const isThisPaneFocused = computed(
  () => !bookViewStore.splitViewEnabled || bookViewStore.focusedPaneId === props.paneId,
)

useEventListener('keydown', (e: KeyboardEvent) => {
  // ── Pane-scoped shortcuts ──────────────────────────────────────────────────
  // Only fire when this pane is focused (or split view is not active).
  if (isThisPaneFocused.value) {
    if (e.ctrlKey && e.code === 'KeyW') {
      e.preventDefault()
      pane.closeTab(pane.activeTabId.value)
      return
    } else if (e.ctrlKey && e.code === 'KeyX') {
      e.preventDefault()
      pane.closeAllTabs()
      return
    } else if (e.ctrlKey && !e.shiftKey && e.code === 'Tab') {
      e.preventDefault()
      const paneTabs = pane.tabs.value
      const currentIndex = paneTabs.findIndex((t) => t.id === pane.activeTabId.value)
      const nextIndex = (currentIndex + 1) % paneTabs.length
      pane.switchTab(paneTabs[nextIndex]!.id)
      return
    } else if (e.ctrlKey && e.shiftKey && e.code === 'Tab') {
      e.preventDefault()
      const paneTabs = pane.tabs.value
      const currentIndex = paneTabs.findIndex((t) => t.id === pane.activeTabId.value)
      const previousIndex = (currentIndex - 1 + paneTabs.length) % paneTabs.length
      pane.switchTab(paneTabs[previousIndex]!.id)
      return
    } else if (e.ctrlKey && e.code === 'KeyB') {
      e.preventDefault()
      if (isBookViewActive.value) {
        bookViewStore.toggleToolbar(props.paneId)
      } else if (activeTab.value?.route === '/pdf-view') {
        pane.togglePdfViewerTitleBar()
      }
      return
    } else if (e.ctrlKey && e.code === 'KeyJ') {
      e.preventDefault()
      if (isBookViewActive.value) bookViewStore.toggleBottomPanel(props.paneId)
      return
    } else if (e.ctrlKey && e.code === 'KeyK') {
      e.preventDefault()
      if (isBookViewActive.value) bookViewStore.toggleTocPanel(props.paneId)
      return
    } else if (e.ctrlKey && e.code === 'KeyF') {
      if (document.activeElement?.closest('[data-ctrlf-enabled]')) return
      e.preventDefault()
      if (isBookViewActive.value) {
        bookViewStore.openSearch(props.paneId)
      } else if (isTxtViewActive.value) {
        bookViewStore.txtViewToggleSearch(props.paneId)
      }
      return
    } else if (e.ctrlKey && e.code === 'KeyT') {
      e.preventDefault()
      // The standalone/demo app shows the tab list in the native chrome strip's
      // dropdown (works in fullscreen); VSTO and the dev browser use the Vue
      // title-bar dropdown.
      if (hasNativeChromeTabs) toggleChromeTabList()
      else toggleTabDropdown()
      return
    } else if (e.ctrlKey && e.code === 'KeyE') {
      // Focus the address bar (Explorer/omnibox-style). Enters search mode; the
      // AddressBar focuses its input on mount.
      e.preventDefault()
      enterSearchMode()
      return
    } else if (e.ctrlKey && e.code === 'KeyN') {
      e.preventDefault()
      pane.openNewTab()
      return
    } else if (e.ctrlKey && e.code === 'KeyG') {
      e.preventDefault()
      pane.goHome()
      return
    } else if (e.ctrlKey && e.code === 'KeyH') {
      e.preventDefault()
      titleBarVisible.value = !titleBarVisible.value
      return
    } else if (e.ctrlKey && e.code === 'KeyL') {
      e.preventDefault()
      themeStore.toggleDarkMode()
      return
    } else if (e.ctrlKey && e.code === 'KeyM') {
      e.preventDefault()
      toggleNavDropdown()
      return
    } else if (e.code === 'F1') {
      e.preventDefault()
      navigateInNewTab('הגדרות')
      return
    } else if (e.ctrlKey && e.code === 'Digit1') {
      e.preventDefault()
      navigateInNewTab('ספרים')
      return
    } else if (e.ctrlKey && e.code === 'Digit2') {
      e.preventDefault()
      navigateInNewTab('חיפוש')
      return
    } else if (e.ctrlKey && e.code === 'Digit3') {
      e.preventDefault()
      navigateInNewTab('היברו-בוקס')
      return
    } else if (e.ctrlKey && e.code === 'Digit4') {
      e.preventDefault()
      navigateInNewTab('פתח קובץ')
      return
    } else if (e.ctrlKey && e.code === 'Digit5') {
      e.preventDefault()
      navigateInNewTab('חיפוש קבצים')
      return
    } else if (e.ctrlKey && e.code === 'Digit6') {
      e.preventDefault()
      navigateInNewTab('מילון')
      return
    } else if (e.ctrlKey && e.code === 'Digit7') {
      e.preventDefault()
      navigateInNewTab('לוח שנה')
      return
    } else if (e.ctrlKey && e.code === 'Digit8') {
      e.preventDefault()
      navigateInNewTab('מידות ושיעורים')
      return
    } else if (e.ctrlKey && e.code === 'Digit9') {
      e.preventDefault()
      navigateInNewTab('סביבות עבודה')
      return
    }
  }

  // ── App-wide shortcuts — pane 1 only ──────────────────────────────────────
  if (props.paneId === 1) {
    if (e.ctrlKey && e.code === 'Backslash') {
      e.preventDefault()
      if (isSplitViewAvailable.value) bookViewStore.toggleSplitView()
    } else if (e.ctrlKey && e.shiftKey && e.code === 'KeyF') {
      e.preventDefault()
      toggleFullscreen()
    } else if (e.code === 'F11') {
      e.preventDefault()
      toggleFullscreen()
    } else if (e.ctrlKey && e.code === 'KeyP') {
      e.preventDefault()
    }
  }
}, { capture: true })
</script>

<template>
  <!-- Keyboard event listener is always active (above), but only render the visual header when titleBarVisible is true -->
  <div ref="barRef" class="title-bar-container" :class="{ hidden: !titleBarVisible }">
    <header class="title-bar" @click="onTitleBarClick" @dblclick="onTitleBarDblClick">
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
            <span class="bar-toc-segment">{{ segment }}</span>
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

  <!-- Tab dropdown — kept outside header so it stays visible when header is hidden -->
  <AppTitleBarTabDropdown
    v-if="dropdownOpen"
    :tabs="pane.tabs.value"
    :active-tab-id="pane.activeTabId.value"
    @select="selectTab"
    @close="pane.closeTab"
    @dismiss="dropdownOpen = false"
    @click.stop
  />

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
  border-bottom: 1px solid var(--border-color);
  position: relative;
  cursor: pointer;
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
  cursor: text;
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
  text-overflow: ellipsis;
  white-space: nowrap;
  flex-shrink: 1;
  min-width: 0;
  margin-inline-end: 2px;
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
