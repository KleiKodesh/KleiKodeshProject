<script setup lang="ts">
import { ref, computed } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useUiChromeVisibility } from '@/composables/useUiChromeVisibility'
import {
  IconLineHorizontal320Regular,
  IconAdd20Regular,
  IconDismiss20Regular,
  IconHome20Regular,
  IconOptions24Regular,
  IconOptions24Filled,
  IconColor24Regular,
  IconColor24Filled,
  IconCrop20Regular,
} from '@iconify-prerendered/vue-fluent'
import ThemeToggle from '@/theme/ThemeToggle.vue'
import AppTitleBarTabDropdown from './AppTitleBarTabDropdown.vue'
import AppTitleBarNavDropdown from './AppTitleBarNavDropdown.vue'
import { useTabStore } from '@/stores/tabStore'
import type { TabRoute } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { usePdfOcrStore } from '@/stores/pdfOcrStore'
import { useThemeStore } from '@/theme/themeStore'
import { toggleFullscreen } from '@/webview-host/bridge'

const bookViewStore = useBookViewStore()
const settingsStore = useSettingsStore()
const tabStore = useTabStore()
const pdfOcrStore = usePdfOcrStore()
const themeStore = useThemeStore()
const { titleBarVisible } = useUiChromeVisibility()

const activeTab = computed(() => tabStore.activeTab)
const dropdownOpen = ref(false)
const navDropdownOpen = ref(false)
const barRef = ref<HTMLElement | null>(null)
const navBtnRef = ref<HTMLElement | null>(null)

const isPdfTab = computed(
  () => activeTab.value?.route === '/pdf-view' || activeTab.value?.route === '/html-view',
)

const barTitle = computed(() => {
  const full = activeTab.value?.tocPath
    ? activeTab.value.title + ' · ' + activeTab.value.tocPath
    : activeTab.value?.title
  return full ? full + '\n(לחץ להצגת רשימת הלשוניות - Alt+T)' : '(לחץ להצגת רשימת הלשוניות - Alt+T)'
})

const toolbarTitle = computed(() => {
  const baseTitle = bookViewStore.isBookViewActive
    ? bookViewStore.toolbarVisible ? 'הסתר סרגל כלים' : 'הצג סרגל כלים'
    : activeTab.value?.pdfViewerTitleBarVisible !== false ? 'הסתר סרגל כותרת PDF' : 'הצג סרגל כותרת PDF'
  return `${baseTitle} (Ctrl+B)`
})

const pdfFilterTitle = computed(() =>
  settingsStore.pdfPageFilters ? 'ביטול פילטרים' : 'החלת פילטרים',
)

const { justClosed } = useDropdownClose(barRef, () => {
  dropdownOpen.value = false
})

const ROUTE_MAP: Record<string, { title: string; route: TabRoute }> = {
  homepage: { title: 'בית', route: '/' },
  openfile: { title: 'ספרים', route: '/books' },
  hebrewbooks: { title: 'היברו-בוקס', route: '/hebrewbooks' },
  search: { title: 'חיפוש', route: '/search' as TabRoute },
}

function toggleTabDropdown() {
  if (justClosed.value) return
  dropdownOpen.value = !dropdownOpen.value
}

function toggleNavDropdown() {
  navDropdownOpen.value = !navDropdownOpen.value
  dropdownOpen.value = false
}

function openNewTab() {
  const target = ROUTE_MAP[settingsStore.newTabPage] ?? { title: 'בית', route: '/' as TabRoute }
  if (target.route === '/') {
    tabStore.openNewHomeTab()
  } else {
    tabStore.openTab({ title: target.title, route: target.route })
  }
}

function selectTab(id: string) {
  tabStore.switchTab(id)
  dropdownOpen.value = false
}

function goHome() {
  const cur = tabStore.activeTabId
  const existing = tabStore.tabs.find((t) => t.route === '/')
  if (existing) {
    if (existing.id !== cur) {
      tabStore.switchTab(existing.id)
      tabStore.closeTab(cur)
    }
  } else {
    tabStore.updateActiveTab({ route: '/', title: 'בית' })
  }
}

// Keyboard event listener — always active, even when title bar is hidden.
// Uses capture phase to intercept shortcuts before child elements (e.g. the book view scroller)
// can consume them with preventDefault().
useEventListener('keydown', (e: KeyboardEvent) => {
  if (e.ctrlKey && e.code === 'KeyW') {
    e.preventDefault()
    tabStore.closeTab(tabStore.activeTabId)
  } else if (e.ctrlKey && e.code === 'KeyX') {
    e.preventDefault()
    tabStore.closeAllTabs()
  } else if (e.ctrlKey && e.code === 'Tab') {
    e.preventDefault()
    dropdownOpen.value = !dropdownOpen.value
  } else if (e.ctrlKey && e.code === 'KeyB') {
    e.preventDefault()
    if (bookViewStore.isBookViewActive) {
      bookViewStore.toggleToolbar()
    } else if (tabStore.activeTab?.route === '/pdf-view') {
      tabStore.togglePdfViewerTitleBar()
    }
  } else if (e.ctrlKey && e.code === 'KeyJ') {
    e.preventDefault()
    if (bookViewStore.isBookViewActive) bookViewStore.toggleBottomPanel()
  } else if (e.ctrlKey && e.shiftKey && e.code === 'KeyF') {
    e.preventDefault()
    toggleFullscreen()
  } else if (e.code === 'F11') {
    e.preventDefault()
    toggleFullscreen()
  } else if (e.ctrlKey && e.code === 'KeyF') {
    e.preventDefault()
    if (bookViewStore.isBookViewActive) {
      // Open search bar in book view from anywhere (no focus required)
      bookViewStore.openSearch()
    }
  } else if (e.ctrlKey && e.code === 'KeyP') {
    e.preventDefault()
  } else if (e.altKey && e.code === 'KeyM') {
    e.preventDefault()
    toggleNavDropdown()
  } else if (e.altKey && e.code === 'KeyT') {
    e.preventDefault()
    toggleTabDropdown()
  } else if (e.altKey && e.code === 'KeyN') {
    e.preventDefault()
    openNewTab()
  } else if (e.altKey && e.code === 'KeyL') {
    e.preventDefault()
    themeStore.toggleDarkMode()
  } else if (e.altKey && e.code === 'Home') {
    e.preventDefault()
    goHome()
  }
}, { capture: true })
</script>

<template>
  <!-- Keyboard event listener is always active (above), but only render the visual header when titleBarVisible is true -->
  <div ref="barRef" class="title-bar-container" :class="{ hidden: !titleBarVisible }">
    <header class="title-bar" @click="toggleTabDropdown">
    <div class="bar-start">
      <div class="nav-btn-wrap">
        <button ref="navBtnRef" class="bar-btn" title="תפריט (Alt+M)" @click.stop="toggleNavDropdown">
          <IconLineHorizontal320Regular />
        </button>
      </div>
      <ThemeToggle @click.stop />
      <button
        v-if="bookViewStore.isBookViewActive || activeTab?.route === '/pdf-view'"
        class="bar-btn"
        :title="toolbarTitle"
        @click.stop="bookViewStore.isBookViewActive ? bookViewStore.toggleToolbar() : tabStore.togglePdfViewerTitleBar()"
      >
        <IconOptions24Filled v-if="bookViewStore.isBookViewActive ? bookViewStore.toolbarVisible : activeTab?.pdfViewerTitleBarVisible !== false" />
        <IconOptions24Regular v-else />
      </button>
      <button
        v-if="isPdfTab"
        class="bar-btn"
        :class="{ active: settingsStore.pdfPageFilters }"
        :title="pdfFilterTitle"
        @click.stop="settingsStore.togglePdfPageFilters()"
      >
        <IconColor24Filled v-if="settingsStore.pdfPageFilters" />
        <IconColor24Regular v-else />
      </button>
    </div>

    <span class="bar-title" :title="barTitle">
      {{ activeTab?.title }}
      <span v-if="activeTab?.tocPath" class="bar-toc-path"> · {{ activeTab?.tocPath }}</span>
    </span>

    <div class="bar-end">
      <button
        v-if="activeTab?.route === '/pdf-view'"
        class="bar-btn"
        :class="{ active: pdfOcrStore.isActive }"
        title="בחירת טקסט באזור (OCR)"
        @click.stop="pdfOcrStore.toggle()"
      >
        <IconCrop20Regular />
      </button>
      <button class="bar-btn" title="בית (Alt+Home)" @click.stop="goHome"><IconHome20Regular /></button>
      <button class="bar-btn" title="לשונית חדשה (Alt+N)" @click.stop="openNewTab">
        <IconAdd20Regular />
      </button>
      <button
        class="bar-btn"
        title="סגור לשונית (Ctrl+W)"
        @click.stop="tabStore.closeTab(tabStore.activeTabId)"
      >
        <IconDismiss20Regular />
      </button>
    </div>

  </header>

  <!-- Tab dropdown — kept outside header so it stays visible when header is hidden -->
  <AppTitleBarTabDropdown
    v-if="dropdownOpen"
    :tabs="tabStore.tabs"
    :active-tab-id="tabStore.activeTabId"
    @select="selectTab"
    @close="tabStore.closeTab"
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
