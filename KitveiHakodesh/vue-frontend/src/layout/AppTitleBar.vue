<script setup lang="ts">
import { ref, computed, defineAsyncComponent } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useDropdownClose } from '@/composables/useDropdownClose'
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
  IconBoardSplit20Regular,
  IconBoardSplit20Filled,
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
import { toggleFullscreen } from '@/webview-host/bridge'
import { useAppNavigation } from '@/composables/useAppNavigation'

const props = withDefaults(defineProps<{ paneId?: 1 | 2 }>(), { paneId: 1 })

const pane = useAppShellPane(props.paneId)
const bookViewStore = useBookViewStore()
const settingsStore = useSettingsStore()
const pdfOcrStore = usePdfOcrStore()
const themeStore = useThemeStore()
const { navigateInNewTab } = useAppNavigation()
const { titleBarVisible } = useUiChromeVisibility()

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
    ? bookViewStore.toolbarVisible ? 'הסתר סרגל כלים' : 'הצג סרגל כלים'
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

function toggleNavDropdown() {
  navDropdownOpen.value = !navDropdownOpen.value
  dropdownOpen.value = false
}

function selectTab(id: string) {
  pane.switchTab(id)
  dropdownOpen.value = false
}

// Keyboard shortcuts — only installed on pane 1's title bar.
// Pane 2 is a secondary reading pane; all keyboard navigation targets pane 1.
// The iframe relay (message handler below) also only fires on pane 1.
if (props.paneId === 1) {

// Forward Ctrl+key shortcuts from child iframes (HTML viewer) back into the
// top-level keydown pipeline.
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

useEventListener('keydown', (e: KeyboardEvent) => {
  if (e.ctrlKey && e.code === 'KeyW') {
    e.preventDefault()
    pane.closeTab(pane.activeTabId.value)
  } else if (e.ctrlKey && e.code === 'KeyX') {
    e.preventDefault()
    pane.closeAllTabs()
  } else if (e.ctrlKey && !e.shiftKey && e.code === 'Tab') {
    e.preventDefault()
    const paneTabs = pane.tabs.value
    const currentIndex = paneTabs.findIndex((t) => t.id === pane.activeTabId.value)
    const nextIndex = (currentIndex + 1) % paneTabs.length
    pane.switchTab(paneTabs[nextIndex]!.id)
  } else if (e.ctrlKey && e.shiftKey && e.code === 'Tab') {
    e.preventDefault()
    const paneTabs = pane.tabs.value
    const currentIndex = paneTabs.findIndex((t) => t.id === pane.activeTabId.value)
    const previousIndex = (currentIndex - 1 + paneTabs.length) % paneTabs.length
    pane.switchTab(paneTabs[previousIndex]!.id)
  } else if (e.ctrlKey && e.code === 'KeyB') {
    e.preventDefault()
    if (isBookViewActive.value) {
      bookViewStore.toggleToolbar()
    } else if (activeTab.value?.route === '/pdf-view') {
      pane.togglePdfViewerTitleBar()
    }
  } else if (e.ctrlKey && e.code === 'KeyJ') {
    e.preventDefault()
    if (isBookViewActive.value) bookViewStore.toggleBottomPanel()
  } else if (e.ctrlKey && e.code === 'KeyK') {
    e.preventDefault()
    if (isBookViewActive.value) bookViewStore.toggleTocPanel()
  } else if (e.ctrlKey && e.shiftKey && e.code === 'KeyF') {
    e.preventDefault()
    toggleFullscreen()
  } else if (e.code === 'F11') {
    e.preventDefault()
    toggleFullscreen()
  } else if (e.ctrlKey && e.code === 'KeyF') {
    if (document.activeElement?.closest('[data-ctrlf-enabled]')) return
    e.preventDefault()
    if (isBookViewActive.value) {
      bookViewStore.openSearch()
    } else if (isTxtViewActive.value) {
      bookViewStore.txtViewToggleSearch()
    }
  } else if (e.ctrlKey && e.code === 'KeyP') {
    e.preventDefault()
  } else if (e.ctrlKey && e.code === 'KeyM') {
    e.preventDefault()
    toggleNavDropdown()
  } else if (e.ctrlKey && e.code === 'KeyT') {
    e.preventDefault()
    toggleTabDropdown()
  } else if (e.ctrlKey && e.code === 'KeyN') {
    e.preventDefault()
    pane.openNewTab()
  } else if (e.ctrlKey && e.code === 'KeyL') {
    e.preventDefault()
    themeStore.toggleDarkMode()
  } else if (e.ctrlKey && e.code === 'KeyG') {
    e.preventDefault()
    pane.goHome()
  } else if (e.code === 'F1') {
    e.preventDefault()
    navigateInNewTab('הגדרות')
  } else if (e.ctrlKey && e.code === 'Digit1') {
    e.preventDefault()
    navigateInNewTab('ספרים')
  } else if (e.ctrlKey && e.code === 'Digit2') {
    e.preventDefault()
    navigateInNewTab('חיפוש')
  } else if (e.ctrlKey && e.code === 'Digit3') {
    e.preventDefault()
    navigateInNewTab('היברו-בוקס')
  } else if (e.ctrlKey && e.code === 'Digit4') {
    e.preventDefault()
    navigateInNewTab('פתח קובץ')
  } else if (e.ctrlKey && e.code === 'Digit5') {
    e.preventDefault()
    navigateInNewTab('חיפוש קבצים')
  } else if (e.ctrlKey && e.code === 'Digit6') {
    e.preventDefault()
    navigateInNewTab('מילון')
  } else if (e.ctrlKey && e.code === 'Digit7') {
    e.preventDefault()
    navigateInNewTab('לוח שנה')
  } else if (e.ctrlKey && e.code === 'Digit8') {
    e.preventDefault()
    navigateInNewTab('מידות ושיעורים')
  } else if (e.ctrlKey && e.code === 'Digit9') {
    e.preventDefault()
    navigateInNewTab('סביבות עבודה')
  }
}, { capture: true })

} // end paneId === 1 keyboard guard
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
        @click.stop="bookViewStore.txtViewToggleSearch()"
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
        @click.stop="isBookViewActive ? bookViewStore.toggleToolbar() : pane.togglePdfViewerTitleBar()"
      >
        <IconOptions24Filled v-if="isBookViewActive ? bookViewStore.toolbarVisible : activeTab?.pdfViewerTitleBarVisible !== false" />
        <IconOptions24Regular v-else />
      </button>
      <button
        v-if="isTitleBarButtonVisible('split-view')"
        class="bar-btn"
        :class="{ active: bookViewStore.splitViewEnabled }"
        :title="bookViewStore.splitViewEnabled ? 'סגור תצוגה מפוצלת' : 'פתח תצוגה מפוצלת'"
        @click.stop="bookViewStore.toggleSplitView()"
      >
        <IconBoardSplit20Filled v-if="bookViewStore.splitViewEnabled" />
        <IconBoardSplit20Regular v-else />
      </button>
    </div>

    <span class="bar-title" :title="barTitle">
      {{ activeTab?.title }}
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
  height: 40px;
  padding: 0 4px;
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
.bar-toc-path {
  color: var(--text-secondary);
  opacity: 0.7;
}
.bar-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
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
