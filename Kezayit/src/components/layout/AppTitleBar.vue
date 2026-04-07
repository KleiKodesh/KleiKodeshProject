<script setup lang="ts">
import { ref, computed } from 'vue'
import { onClickOutside, useEventListener } from '@vueuse/core'
import {
  IconLineHorizontal320Regular,
  IconAdd20Regular,
  IconDismiss20Regular,
  IconHome20Regular,
  IconOptions24Regular,
  IconOptions24Filled,
  IconColor24Regular,
  IconColor24Filled,
} from '@iconify-prerendered/vue-fluent'
import ThemeToggle from '@/theme/ThemeToggle.vue'
import AppTitleBarTabDropdown from './AppTitleBarTabDropdown.vue'
import AppTitleBarNavDropdown from './AppTitleBarNavDropdown.vue'
import { useTabStore } from '@/stores/tabStore'
import type { TabRoute } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'

const bookViewStore = useBookViewStore()
const settingsStore = useSettingsStore()
const tabStore = useTabStore()

const activeTab = computed(() => tabStore.activeTab)
const dropdownOpen = ref(false)
const navDropdownOpen = ref(false)
const barRef = ref<HTMLElement | null>(null)

const isPdfTab = computed(
  () => activeTab.value?.route === '/pdf-view' || activeTab.value?.route === '/hebrewbooks',
)

const barTitle = computed(() => {
  const full = activeTab.value?.tocPath
    ? activeTab.value.title + ' · ' + activeTab.value.tocPath
    : activeTab.value?.title
  return full ? full + '\n(לחץ להצגת רשימת הלשוניות)' : '(לחץ להצגת רשימת הלשוניות)'
})

const toolbarTitle = computed(() =>
  bookViewStore.toolbarVisible ? 'הסתר סרגל כלים' : 'הצג סרגל כלים',
)

const pdfFilterTitle = computed(() =>
  settingsStore.pdfPageFilters ? 'ביטול פילטרים לעמודי PDF' : 'החלת פילטרים לעמודי PDF',
)

onClickOutside(barRef, () => {
  dropdownOpen.value = false
})

const ROUTE_MAP: Record<string, { title: string; route: TabRoute }> = {
  homepage: { title: 'בית', route: '/' },
  openfile: { title: 'ספרים', route: '/books' },
  hebrewbooks: { title: 'היברו-בוקס', route: '/hebrewbooks' },
  'kezayit-search': { title: 'חיפוש', route: '/search' as TabRoute },
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

useEventListener('keydown', (e: KeyboardEvent) => {
  if (e.ctrlKey && e.key === 'w') {
    e.preventDefault()
    tabStore.closeTab(tabStore.activeTabId)
  } else if (e.ctrlKey && e.key === 'x') {
    e.preventDefault()
    tabStore.closeAllTabs()
  } else if (e.ctrlKey && e.key === 'j') {
    e.preventDefault()
    if (bookViewStore.isBookViewActive) bookViewStore.toggleBottomPanel()
  } else if (e.ctrlKey && e.key === 'f') {
    const active = document.activeElement as HTMLElement | null
    if (!active?.dataset.ctrlfEnabled) e.preventDefault()
  }
})
</script>

<template>
  <header ref="barRef" class="title-bar" @click="dropdownOpen = !dropdownOpen">
    <div class="bar-start">
      <div class="nav-btn-wrap">
        <button class="bar-btn" @click.stop="toggleNavDropdown">
          <IconLineHorizontal320Regular />
        </button>
        <AppTitleBarNavDropdown
          v-if="navDropdownOpen"
          @close="navDropdownOpen = false"
          @click.stop
        />
      </div>
      <ThemeToggle @click.stop />
      <button
        v-if="bookViewStore.isBookViewActive"
        class="bar-btn"
        :title="toolbarTitle"
        @click.stop="bookViewStore.toggleToolbar"
      >
        <IconOptions24Filled v-if="bookViewStore.toolbarVisible" />
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
      <button class="bar-btn" title="בית" @click.stop="goHome"><IconHome20Regular /></button>
      <button class="bar-btn" title="לשונית חדשה" @click.stop="openNewTab">
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

    <!-- Tab list dropdown — opens on click anywhere on the title bar; all icons use Regular (non-filled) variants -->
    <AppTitleBarTabDropdown
      v-if="dropdownOpen"
      :tabs="tabStore.tabs"
      :active-tab-id="tabStore.activeTabId"
      @select="selectTab"
      @close="tabStore.closeTab"
      @dismiss="dropdownOpen = false"
      @click.stop
    />
  </header>
</template>

<style scoped>
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
}
</style>
