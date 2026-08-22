<script setup lang="ts">
import { computed } from 'vue'
import {
  IconChevronDoubleRight20Regular,
  IconOpen28Regular,
  IconSplitVertical20Regular,
  IconSplitVertical20Filled,
} from '@iconify-prerendered/vue-fluent'
import { APP_NAV_ITEMS, APP_NAV_SETTINGS_ITEM } from './appNavItems'
import { useAppNavigation } from '@/composables/useAppNavigation'
import { useSplitViewAvailable } from './useSplitViewAvailable'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { showPopOutButton, togglePopOut } from '@/webview-host/bridge'

// The nav dropdown's items, always on. Same list, same order, same actions - the only
// difference is that a row here is its icon alone with the label as its tooltip, because
// the rail is deliberately too narrow for text. Whether it is showing is a setting
// (settingsStore.navSidebarVisible), so it survives a restart.
//
// One rail per pane, like the title bar above it: navigateInNewTab goes through
// usePaneNavigation, so each rail opens its destination in its own pane's tabs.
const { navigateInNewTab } = useAppNavigation()
const bookViewStore = useBookViewStore()
const settingsStore = useSettingsStore()

// The split-view toggle moves in here while the rail is up - the title bar drops it, the
// way it drops the hamburger - so a window control never sits in two places at once. It
// still answers to the same visibility setting as when it lives in the title bar.
const isSplitViewAvailable = useSplitViewAvailable()
const isSplitViewButtonVisible = computed(
  () => isSplitViewAvailable.value && !settingsStore.titleBarHiddenButtons.includes('split-view'),
)
const splitViewTitle = computed(() =>
  bookViewStore.splitViewEnabled ? 'סגור תצוגה מפוצלת (Ctrl+|)' : 'פתח תצוגה מפוצלת (Ctrl+|)',
)
</script>

<template>
  <nav class="nav-sidebar">
    <button
      v-for="item in APP_NAV_ITEMS"
      :key="item.label"
      class="nav-btn"
      tabindex="-1"
      :title="`${item.label} (${item.shortcut})`"
      @click="navigateInNewTab(item.label)"
    >
      <component :is="item.icon" :style="item.color ? { color: item.color } : {}" />
    </button>
    <!-- Second group, pinned to the far end: settings and the rail's own controls, which
         are not destinations. Space is what separates the two groups - a rule here would
         cut the one surface into two. -->
    <div class="nav-group-end">
      <button
        v-if="isSplitViewButtonVisible"
        class="nav-btn nav-btn-sm"
        tabindex="-1"
        :title="splitViewTitle"
        @click="bookViewStore.toggleSplitView()"
      >
        <IconSplitVertical20Filled v-if="bookViewStore.splitViewEnabled" />
        <IconSplitVertical20Regular v-else />
      </button>
      <button
        v-if="showPopOutButton"
        class="nav-btn"
        tabindex="-1"
        title="פתח בחלון עצמאי או החזר לחלונית"
        @click="togglePopOut()"
      >
        <IconOpen28Regular />
      </button>
      <!-- Settings is always the last item before the rail's own collapse control - it is
           the floor of the rail, everything else stacks above it. -->
      <button
        class="nav-btn"
        tabindex="-1"
        :title="`${APP_NAV_SETTINGS_ITEM.label} (${APP_NAV_SETTINGS_ITEM.shortcut})`"
        @click="navigateInNewTab(APP_NAV_SETTINGS_ITEM.label)"
      >
        <component :is="APP_NAV_SETTINGS_ITEM.icon" />
      </button>
      <!-- The ONLY way to close the rail - the menu row that opened it is gone while the
           rail is up (AppTitleBar drops the hamburger and Ctrl+M). -->
      <button
        class="nav-btn nav-btn-sm"
        tabindex="-1"
        title="הסתר סרגל צד"
        @click="settingsStore.navSidebarVisible = false"
      >
        <IconChevronDoubleRight20Regular />
      </button>
    </div>
  </nav>
</template>

<style scoped>
.nav-sidebar {
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
  /* Sizing is taken from where the reference's hover background actually lands: the item
     is the 24px glyph plus a 4px inset all round - 32px - and the rail is that plus a 6px
     gutter each side. The item is NOT the full width of the rail; that reading came from
     the icon spacing rather than from the hover band, and made every target 12px too big.
     The vertical gap between items is what is left of the 44px pitch. */
  width: 44px;
  padding: 6px;
  gap: 6px;
  /* No surface of its own and no border. The rail is the app's frame thickened down this
     side, not a panel inside the app: it takes the title bar's --bg-secondary by
     inheriting it from .app-shell, and nothing is drawn between the two. What marks the
     rail out is the column of items in it, not an edge around it. */
  overflow-y: auto;
  scrollbar-width: none;
}
.nav-sidebar::-webkit-scrollbar {
  display: none;
}

.nav-group-end {
  display: flex;
  flex-direction: column;
  margin-top: auto;
}

.nav-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  /* 32px square: the glyph plus an even 4px inset, which is exactly the hover band in the
     reference. Keeps the app's 4px radius - the band is a rounded square, not a
     full-width stripe. */
  width: 32px;
  height: 32px;
  border-radius: 4px;
  flex-shrink: 0;
}
/* 24px is the size these glyphs are drawn for (documentIcons hands out icon24), so they
   land on the pixel grid instead of being a shrunken 24. The colour is inherited because
   theme.css pins `svg { color: ... }`, which otherwise cuts the glyph off from the
   button's own colour and the hover colour never reaches it; the colourful items set
   their colour inline, which still wins over this. */
.nav-btn svg {
  width: 24px;
  height: 24px;
  color: inherit;
}
/* Glyphs Fluent only ships at 20 (the double chevron, the split-view pair) are drawn at
   20 rather than scaled up to the rail's 24. */
.nav-btn-sm svg {
  width: 20px;
  height: 20px;
}
</style>
