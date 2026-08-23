<script setup lang="ts">
import { computed, inject } from 'vue'
import {
  IconChevronDoubleRight20Regular,
  IconOpen28Regular,
  IconSplitVertical20Regular,
  IconSplitVertical20Filled,
} from '@iconify-prerendered/vue-fluent'
import { APP_NAV_ITEMS, APP_NAV_SETTINGS_ITEM } from './appNavItems'
import { documentIcon } from '@/utils/documentIcons'
import { useAppShellPane } from '@/composables/useAppShellPane'
import { useAppNavigation } from '@/composables/useAppNavigation'
import { useSplitViewAvailable } from './useSplitViewAvailable'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { showPopOutButton, togglePopOut } from '@/webview-host/bridge'

// The nav dropdown's items, always on. Same list, same order, same actions - the only
// difference is that a row here is its icon alone with the label as its tooltip, because
// the rail is deliberately too narrow for text. Whether it is showing is a setting
// (settingsStore, keyed by pane), so it survives a restart. Each pane's rail is its own:
// closing this one never touches the other pane's.
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

// Which pane this rail belongs to - it closes its own, not both, and goes home in its own.
const paneId = inject<1 | 2>('paneId', 1)
const pane = useAppShellPane(paneId)

// Home is not one of APP_NAV_ITEMS: those are destinations opened in a new tab by label,
// and home is not one - goHome() reuses the pane's existing home tab if it has one rather
// than stacking up another. Same reason the title bar keeps its own home button. The icon
// still comes from the shared table so it matches the rest of the column.
const homeIcon = documentIcon('home').icon24
</script>

<template>
  <!-- The strip holds the width in the layout; the panel inside it is the docked sheet,
       full width and inset only at the top and bottom. -->
  <div class="nav-sidebar">
    <nav class="nav-panel">
      <button
        class="nav-btn"
        tabindex="-1"
        title="בית (Ctrl+G)"
        @click="pane.goHome()"
      >
        <component :is="homeIcon" />
      </button>
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
          @click="settingsStore.setNavSidebarVisible(paneId, false)"
        >
          <IconChevronDoubleRight20Regular />
        </button>
      </div>
    </nav>
  </div>
</template>

<style scoped>
/* The strip is what occupies the layout; the panel inside it is the absolutely-positioned
   sheet that fills it.

   Panel sizing is taken from where the reference's hover background actually lands: the
   item is the 24px glyph plus a 4px inset all round - 32px - and the panel is that plus a
   6px gutter each side, 44px. The item is NOT the full width of the panel; that reading
   came from the icon spacing rather than from the hover band, and made every target 12px
   too big. The vertical gap between items is what is left of the 44px pitch. */
.nav-sidebar {
  --nav-panel-width: 44px;
  --nav-panel-inset: 6px;
  position: relative;
  flex-shrink: 0;
  /* The panel fills the strip's width - the inset is vertical only, so there is no side gap
     to leave room for and no surface showing through beside it. */
  width: var(--nav-panel-width);
  /* Above the content panel it sits over. */
  z-index: 20;
}

/* The app menu as a sheet docked to this pane's edge: the same surface, frame and radius as
   the hamburger dropdown that offers these same items (AppTitleBarNavDropdown), so it reads
   as that menu rather than as the app's frame thickened down one side.

   Full width of its strip, inset at the top and bottom only, and square on the side against
   the pane's edge - the two rounded corners face the page, which is the only side there is
   anything to round against. The side insets went and took a fair amount with them: with a
   gap beside the panel, something had to paint that gap (the shell behind it is the panel's
   own colour, so it showed as nothing). None of that is needed once the panel fills its
   column.

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
  /* Inset at the top and bottom only, which is all the rounded corners need to read as
     corners. No side margin: the sheet runs the full width of its strip, flush to the
     pane's edge on one side and to the content on the other. */
  margin-block: var(--nav-panel-inset);
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  /* Nothing drawn on the side against the pane's edge - no border there, and square corners
     - so the sheet runs straight off that edge instead of closing itself off a pixel short
     of it. The frame and the two rounded corners face the page, which is the only side
     there is anything to frame against. PHYSICAL right, deliberately, not the logical
     properties: the document is dir=rtl, where the logical end edge maps to the physical
     LEFT - the side the page is on. */
  border-right: none;
  border-radius: 6px 0 0 6px;
  overflow-y: auto;
  scrollbar-width: none;
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
