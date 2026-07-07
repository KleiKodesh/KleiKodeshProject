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
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { usePdfOcrStore } from '@/stores/pdfOcrStore'
import { useThemeStore } from '@/theme/themeStore'
import { toggleFullscreen, isVstoEnvironment as isVsto } from '@/webview-host/bridge'

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

const barTitle = computed(() => {
  const full = activeTab.value?.tocPath
    ? activeTab.value.title + ' · ' + activeTab.value.tocPath
    : activeTab.value?.title
  return full ? full + '\n(לחץ להצגת רשימת הלשוניות - Ctrl+T)' : '(לחץ להצגת רשימת הלשוניות - Ctrl+T)'
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

function toggleTabDropdown() {
  dropdownOpen.value = !dropdownOpen.value
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
      toggleTabDropdown()
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
    <header class="title-bar" @click="toggleTabDropdown">
    <div class="bar-start">
      <div class="nav-btn-wrap">
        <button
          v-if="isTitleBarButtonVisible('hamburger')"
          ref="navBtnRef"
          class="bar-btn"
          title="תפריט (Ctrl+M)"
          @click.stop="toggleNavDropdown"
        >
          <IconLineHorizontal320Regular />
        </button>
      </div>
      <ThemeToggle v-if="isTitleBarButtonVisible('theme-toggle')" />
      <button
        v-if="isTxtViewActive"
        class="bar-btn"
        title="חיפוש בטקסט (Ctrl+F)"
        @click.stop="bookViewStore.txtViewToggleSearch(props.paneId)"
      >
        <IconSearch24Regular />
      </button>
      <button
        v-if="isTitleBarButtonVisible('pdf-filter') && isPdfTab"
        class="bar-btn"
        :title="pdfFilterTitle"
        @click.stop="settingsStore.togglePdfPageFilters()"
      >
        <IconColor24Filled v-if="settingsStore.pdfPageFilters" />
        <IconColor24Regular v-else />
      </button>
      <button
        v-if="isTitleBarButtonVisible('toolbar-toggle') && (isBookViewActive || activeTab?.route === '/pdf-view')"
        class="bar-btn"
        :title="toolbarTitle"
        @click.stop="isBookViewActive ? bookViewStore.toggleToolbar(props.paneId) : pane.togglePdfViewerTitleBar()"
      >
        <IconOptions24Filled v-if="isBookViewActive ? bookViewStore.getToolbarVisible(props.paneId) : activeTab?.pdfViewerTitleBarVisible !== false" />
        <IconOptions24Regular v-else />
      </button>
      <button
        v-if="isTitleBarButtonVisible('split-view') && isSplitViewAvailable"
        class="bar-btn"
        :title="bookViewStore.splitViewEnabled ? 'סגור תצוגה מפוצלת (Ctrl+|)' : 'פתח תצוגה מפוצלת (Ctrl+|)'"
        @click.stop="bookViewStore.toggleSplitView()"
      >
        <IconSplitVertical20Filled v-if="bookViewStore.splitViewEnabled" />
        <IconSplitVertical20Regular v-else />
      </button>
    </div>

    <span class="bar-title" dir="rtl" :title="barTitle">
      <span class="bar-title-name">{{ activeTab?.title }}</span>
      <span v-if="activeTab?.tocPath" class="bar-toc-path"> · {{ activeTab?.tocPath }}</span>
    </span>

    <div class="bar-end">
      <button
        v-if="isTitleBarButtonVisible('ocr') && activeTab?.route === '/pdf-view'"
        class="bar-btn"
        :class="{ active: pdfOcrStore.isActive }"
        title="בחירת טקסט באזור (OCR)"
        @click.stop="pdfOcrStore.toggle()"
      >
        <IconConvertToText24Regular />
      </button>
      <button v-if="isTitleBarButtonVisible('home')" class="bar-btn" title="בית (Ctrl+G)" @click.stop="pane.goHome()"><IconHome20Regular /></button>
      <button v-if="isTitleBarButtonVisible('new-tab')" class="bar-btn" title="לשונית חדשה (Ctrl+N)" @click.stop="pane.openNewTab()">
        <IconAdd20Regular />
      </button>
      <button
        v-if="isTitleBarButtonVisible('close-tab')"
        class="bar-btn"
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
    :toggle-button-el="barRef"
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
  flex: 1;
}
.nav-btn-wrap {
  position: relative;
}
.bar-end {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 0;
  flex: 1;
}
.bar-title {
  font-weight: 400;
  font-size: 0.82rem;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.bar-title-name {
  unicode-bidi: isolate;
  direction: ltr;
}
.bar-toc-path {
  color: var(--text-secondary);
  opacity: 0.7;
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
