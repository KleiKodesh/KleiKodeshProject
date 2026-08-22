<script setup lang="ts">
import { computed, inject } from 'vue'
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
// One rail per pane, like the title bar beside it: navigateInNewTab goes through
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

// Only pane 1 owns the window's edge. In split view pane 2 sits inboard of the sash, so
// its panel is not against anything to run flush into - it closes its frame on all four
// sides and keeps its inset, or it would read as a card sliced in half mid-window.
const paneId = inject<1 | 2>('paneId', 1)
const isDockedToWindowEdge = computed(() => paneId === 1)
</script>

<template>
  <!-- The strip holds the width in the layout; the panel inside it is the docked sheet,
       inset from three sides and flush to the app's edge on the fourth. -->
  <div class="nav-sidebar">
    <nav class="nav-panel" :class="{ 'is-window-edge': isDockedToWindowEdge }">
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
  </div>
</template>

<style scoped>
/* The strip is what occupies the layout; the panel inside it is the absolutely-positioned
   sheet, so the strip has to be as wide as the panel plus whichever of its side insets are
   actually there - one for the pane docked to the window edge, two for the pane that is
   inboard of the sash.

   Panel sizing is taken from where the reference's hover background actually lands: the
   item is the 24px glyph plus a 4px inset all round - 32px - and the panel is that plus a
   6px gutter each side, 44px. The item is NOT the full width of the panel; that reading
   came from the icon spacing rather than from the hover band, and made every target 12px
   too big. The vertical gap between items is what is left of the 44px pitch. */
.nav-sidebar {
  --nav-panel-width: 44px;
  /* Inset on both sides; the window-edge pane drops one below. --nav-panel-inset itself is
     global (main.css) because the title bar matches its vertical padding to it. */
  --nav-panel-side-insets: 2;
  position: relative;
  flex-shrink: 0;
  width: calc(var(--nav-panel-width) + var(--nav-panel-inset) * var(--nav-panel-side-insets));
  /* Above the content panel it sits over. */
  z-index: 20;
}
.nav-sidebar:has(.nav-panel.is-window-edge) {
  --nav-panel-side-insets: 1;
}

/* The app menu as a sheet docked to this pane's edge, like the FTS filter panel over its
   results (FullTextSearchFilterPanel): the same surface, frame and radius as the hamburger
   dropdown that offers these same items (AppTitleBarNavDropdown), inset from the page so
   the chrome shows around it rather than merging into the title bar.

   Framed and inset on all four sides by default, which is what pane 2 needs - in split view
   it sits inboard of the sash, with page on both sides of it. Only the pane that owns the
   window's edge opens that side up (.is-window-edge below).

   Icons only, and it stays that way: the labels live in the tooltips, and the panel does
   not widen on hover. A width that changed under the pointer moved every icon the moment
   you went to click one. */
.nav-panel {
  position: absolute;
  /* PHYSICAL top/bottom/right, deliberately, not the logical inset-* properties: the
     document is dir=rtl, where the logical end edge maps to the physical LEFT - which
     pinned the wrong side. The panel is docked to a physical edge of the window, so the
     physical property is the one that says it. */
  top: 0;
  bottom: 0;
  right: 0;
  width: var(--nav-panel-width);
  display: flex;
  flex-direction: column;
  padding: var(--nav-panel-inset);
  gap: var(--nav-panel-inset);
  margin: var(--nav-panel-inset);
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  overflow-y: auto;
  scrollbar-width: none;
}
/* The pane that owns the window's right edge runs flat into it: no margin, no border and
   no radius on that side, so the sheet reads as having slid in from off-screen rather than
   as a card floating a few pixels short of the frame. PHYSICAL `right`, deliberately - the
   document is dir=rtl, where the logical end edge maps to the physical LEFT. */
.nav-panel.is-window-edge {
  margin-right: 0;
  border-right: none;
  border-radius: 6px 0 0 6px;
}
.nav-panel::-webkit-scrollbar {
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
