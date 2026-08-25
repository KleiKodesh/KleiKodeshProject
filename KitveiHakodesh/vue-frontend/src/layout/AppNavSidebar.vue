<script setup lang="ts">
import { computed, inject, ref, watch } from 'vue'
import {
  IconChevronDoubleRight20Regular,
  IconMoreHorizontal24Regular,
  IconOpen28Regular,
  IconSplitVertical20Regular,
  IconSplitVertical20Filled,
} from '@iconify-prerendered/vue-fluent'
import { APP_NAV_SETTINGS_ITEM } from './appNavItems'
import AppNavSidebarOverflowMenu from './AppNavSidebarOverflowMenu.vue'
import { useAppNavSidebarOverflow } from './useAppNavSidebarOverflow'
import { useAppNavigation } from '@/composables/useAppNavigation'
import { useSplitViewAvailable } from './useSplitViewAvailable'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { togglePopOut } from '@/webview-host/bridge'

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

// Which pane this rail belongs to - it closes its own rail, not both panes'.
const paneId = inject<1 | 2>('paneId', 1)

// Home and workspaces are both deliberately NOT on the rail: they live in the title bar,
// side by side, and only there. The hamburger and the split-view toggle move in here
// while the rail is up because they are about the rail and the window, so the rail can
// speak for them - but home is a destination and workspaces is a picker, and each had one
// place already. What is left on the rail is the destinations and its own controls.

/** The rail's own sheet - the surface the overflow flyout must open beside, never over. */
const navPanelEl = ref<HTMLElement | null>(null)

// ── Vertical overflow ─────────────────────────────────────────────────────────
//
// Buttons a too-short rail cannot fit collapse, bottom-up, into a "more" button whose
// flyout lists them with their labels. The one exception is the hide button, which keeps
// the rail's floor - it is the only way to close the rail, and a control that vanished
// exactly when the rail got cramped would trap it open. The composable owns the fit
// arithmetic and the flat key list; what stays here is the flyout's state and what each
// collapsed row does when picked.
const { hasNavOverflow, railButtonVisible, visibleNavItems, overflowedRailKeys } =
  useAppNavSidebarOverflow(navPanelEl, isSplitViewButtonVisible)

const overflowOpen = ref(false)
const overflowButtonEl = ref<HTMLElement | null>(null)

// Every collapsed row's action lives here, not in the flyout - it renders the rows and
// reports the picked key back. A key with no branch is a destination (settings included),
// keyed by its label, which IS the routing key.
function onOverflowRowSelect(key: string) {
  if (key === 'split-view') bookViewStore.toggleSplitView()
  else if (key === 'pop-out') togglePopOut()
  else navigateInNewTab(key)
}

// A resize that gives the room back unmounts the more button - its flyout must not be
// left floating beside a button that no longer exists.
watch(hasNavOverflow, (has) => {
  if (!has) overflowOpen.value = false
})
</script>

<template>
  <!-- The strip holds the width in the layout; the panel inside it is the docked sheet,
       full width and inset only at the top and bottom. -->
  <div class="nav-sidebar">
    <nav ref="navPanelEl" class="nav-panel">
      <button
        v-for="item in visibleNavItems"
        :key="item.label"
        class="nav-btn"
        tabindex="-1"
        :title="`${item.label} (${item.shortcut})`"
        @click="navigateInNewTab(item.label)"
      >
        <component :is="item.icon" :style="item.color ? { color: item.color } : {}" />
      </button>
      <!-- Space is what separates the destinations from the rail's own controls - a rule
           here would cut the one surface into two. A real element rather than an auto
           margin, and every button below it on the same 38px pitch as those above: the
           overflow arithmetic (useAppNavSidebarOverflow) counts every child on one pitch,
           and even at zero height this spacer costs the one extra flex gap it budgets. -->
      <div class="nav-spacer" />
      <button
        v-if="railButtonVisible('split-view')"
        class="nav-btn nav-btn-sm"
        tabindex="-1"
        :title="splitViewTitle"
        @click="bookViewStore.toggleSplitView()"
      >
        <IconSplitVertical20Filled v-if="bookViewStore.splitViewEnabled" />
        <IconSplitVertical20Regular v-else />
      </button>
      <button
        v-if="railButtonVisible('pop-out')"
        class="nav-btn"
        tabindex="-1"
        title="פתח בחלון עצמאי או החזר לחלונית"
        @click="togglePopOut()"
      >
        <IconOpen28Regular />
      </button>
      <button
        v-if="railButtonVisible(APP_NAV_SETTINGS_ITEM.label)"
        class="nav-btn"
        tabindex="-1"
        :title="`${APP_NAV_SETTINGS_ITEM.label} (${APP_NAV_SETTINGS_ITEM.shortcut})`"
        @click="navigateInNewTab(APP_NAV_SETTINGS_ITEM.label)"
      >
        <component :is="APP_NAV_SETTINGS_ITEM.icon" />
      </button>
      <!-- Stands in for the collapsed tail above it, directly over the hide button. Only
           rendered while something has overflowed. -->
      <button
        v-if="hasNavOverflow"
        ref="overflowButtonEl"
        class="nav-btn"
        :class="{ 'nav-btn--on': overflowOpen }"
        tabindex="-1"
        title="פריטים נוספים"
        :aria-expanded="overflowOpen"
        @click="overflowOpen = !overflowOpen"
      >
        <IconMoreHorizontal24Regular />
      </button>
      <!-- Never collapses, and keeps the floor: this is the ONLY way to close the rail -
           the menu row that opened it is gone while the rail is up (AppTitleBar drops the
           hamburger and Ctrl+M) - so a rail short enough to fold it away would be a rail
           nobody could close. -->
      <button
        class="nav-btn nav-btn-sm"
        tabindex="-1"
        title="הסתר סרגל צד"
        @click="settingsStore.setNavSidebarVisible(paneId, false)"
      >
        <IconChevronDoubleRight20Regular />
      </button>
    </nav>
    <!-- `keep-clear-of` is the rail itself: the flyout opens beside it, never over it, even
         in a window too narrow for both - there the panel narrows instead. -->
    <AppNavSidebarOverflowMenu
      v-model:open="overflowOpen"
      :anchor="overflowButtonEl"
      :keep-clear-of="navPanelEl"
      :collapsed-keys="overflowedRailKeys"
      @select="onOverflowRowSelect"
    />
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
  /* The VERTICAL inset, and the gap between buttons. Deliberately not shared with the
     inline axis: it is half of the pitch the overflow arithmetic mirrors, so it must not
     move when the rail goes compact. */
  --nav-panel-inset: 6px;
  --nav-panel-inline-inset: 6px;
  --nav-btn-width: 32px;
  --nav-glyph-size: 24px;
  --nav-glyph-size-sm: 20px;
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
   you went to click one.

   The 32px button and this 6px gap are mirrored as constants in useAppNavSidebarOverflow -
   change either here, change it there. */
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
  padding: var(--nav-panel-inset) var(--nav-panel-inline-inset);
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

/* The empty stretch between the destinations and the controls. It takes whatever height is
   spare, so when the rail is short it is exactly zero and the column is packed - which is
   precisely when the overflow collapse starts. */
.nav-spacer {
  flex: 1 1 0;
  min-height: 0;
}

.nav-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  /* 32px square at full size: the glyph plus an even 4px inset, which is exactly the hover
     band in the reference. Keeps the app's 4px radius - the band is a rounded square, not a
     full-width stripe. The compact rail narrows it; see the container query at the end. */
  width: var(--nav-btn-width);
  /* Pinned at 32, never a var: this is the NAV_BUTTON_HEIGHT the overflow arithmetic
     mirrors. The compact rail narrows and nothing else, so how many buttons fit is the
     same answer at every width. */
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
  width: var(--nav-glyph-size);
  height: var(--nav-glyph-size);
  color: inherit;
}

/* Glyphs Fluent only ships at 20 (the double chevron, the split-view pair) are drawn at
   20 rather than scaled up to the rail's 24. */
.nav-btn-sm svg {
  width: var(--nav-glyph-size-sm);
  height: var(--nav-glyph-size-sm);
}

/* Held down while its submenu is up, so the rail says which button the panel belongs to. */
.nav-btn--on {
  background: color-mix(in srgb, var(--text-primary) 10%, transparent);
}

/* The compact rail. A pane narrow enough that 44px of it is a real share of the reading
   width gets the same rail drawn tighter: a 26px target on a 4px gutter, 36px overall, with
   the glyphs dropped a size so they still sit in their target rather than filling it edge
   to edge. Everything else - the order, the tooltips, the flyout - is unchanged.

   `app-pane` is the PANE's width, not the window's (AppShell declares it): a pane in split
   view is a fraction of the window, and a narrow pane inside a wide window wants the
   compact rail just as much.

   Only the inline axis moves. The button height, the vertical padding and the gap are what
   useAppNavSidebarOverflow mirrors to decide how many buttons fit, so touching them here
   would make the two disagree and hide a button behind a scrollbar that is deliberately
   invisible. Narrower, never shorter. */
@container app-pane (max-width: 480px) {
  .nav-sidebar {
    --nav-panel-width: 36px;
    --nav-panel-inline-inset: 4px;
    --nav-btn-width: 26px;
    --nav-glyph-size: 20px;
    --nav-glyph-size-sm: 18px;
  }
}
</style>
