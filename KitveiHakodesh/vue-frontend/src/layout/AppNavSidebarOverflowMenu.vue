<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import type { Component } from 'vue'
import { useEventListener } from '@vueuse/core'
import {
  IconOpen28Regular,
  IconSplitVertical20Filled,
  IconSplitVertical20Regular,
} from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useBookViewStore } from '@/stores/bookViewStore'
import { APP_NAV_ITEMS, APP_NAV_SETTINGS_ITEM } from './appNavItems'

/**
 * The buttons a too-short rail could not show, as a labelled flyout off the rail's "more"
 * button. Rows look like the hamburger menu's (AppTitleBarNavDropdown) - icon, label,
 * shortcut - because these ARE those rows: the same buttons, just the ones the rail had
 * no room for.
 *
 * The split of duties with the rail: the rail decides WHAT collapsed (`collapsedKeys`, in
 * rail order, from useAppNavSidebarOverflow - which never includes the hide button, the
 * one control pinned to the rail's floor) and owns what every row DOES - `select` reports
 * the picked key back and the rail dispatches. This menu owns each key's FACE:
 * label, icon, shortcut, read off the same tables the rail's buttons read. Every row is
 * an action - the menu never opens anything of its own.
 *
 * Deliberately simpler placement than WorkspaceSubmenu, its floating sibling: one host,
 * one possible side - the rail is docked to the window's physical right edge, so inward
 * (left) is the only direction there is. What remains is the vertical clamp, which is the
 * whole reason it exists: it only ever opens in a window too short for the rail.
 *
 * Teleported to the body like every floating panel here: the rail is a scroll box with
 * its own stacking context, and a panel positioned inside it would be clipped by it.
 */
const props = defineProps<{
  open: boolean
  /** The rail's "more" button. */
  anchor: HTMLElement | null
  /** The rail's own sheet - the panel opens beside it, never over it. */
  keepClearOf: HTMLElement | null
  /** The keys of the buttons that fell off the rail, in their rail order. */
  collapsedKeys: string[]
}>()

const emit = defineEmits<{ 'update:open': [boolean]; select: [key: string] }>()

/** Breathing room kept between the panel and the viewport edges. */
const VIEWPORT_MARGIN = 8
/** Gap between the rail's sheet and the panel - two framed sheets must not touch. */
const KEEP_CLEAR_GAP = 6

const panelRef = ref<HTMLElement | null>(null)
const top = ref(0)
const left = ref(0)
/** Hidden for the first frame: `place()` has to measure the panel before it can place it. */
const placed = ref(false)

/** One collapsed button as a row. */
interface OverflowRow {
  key: string
  label: string
  icon: Component
  color?: string
  shortcut?: string
}

const bookViewStore = useBookViewStore()

const rows = computed<OverflowRow[]>(() => props.collapsedKeys.map(overflowRow))

// The same face the rail's buttons wear - the split-view row is the one that changes
// with state, so its label and icon read the store here just as the rail button's do.
function overflowRow(key: string): OverflowRow {
  if (key === 'split-view') {
    const enabled = bookViewStore.splitViewEnabled
    return {
      key,
      label: enabled ? 'סגור תצוגה מפוצלת' : 'פתח תצוגה מפוצלת',
      shortcut: 'Ctrl+|',
      icon: enabled ? IconSplitVertical20Filled : IconSplitVertical20Regular,
    }
  }
  if (key === 'pop-out') return { key, label: 'חלון עצמאי / חלונית', icon: IconOpen28Regular }
  // A destination, keyed by its label - settings included.
  const item = APP_NAV_ITEMS.find((navItem) => navItem.label === key) ?? APP_NAV_SETTINGS_ITEM
  return { key, label: item.label, shortcut: item.shortcut, icon: item.icon, color: item.color }
}

function rowTitle(row: OverflowRow) {
  return row.shortcut ? `${row.label} (${row.shortcut})` : row.label
}

// The anchor is passed as the toggle so the composable leaves a click on it to the
// button's own @click, instead of closing on pointerdown and reopening on click.
useDropdownClose(panelRef, () => close(), {
  toggleButton: computed(() => props.anchor),
  enabled: () => props.open,
})

function close() {
  emit('update:open', false)
}

function onRowClick(row: OverflowRow) {
  close()
  emit('select', row.key)
}

function place() {
  const anchorEl = props.anchor
  const panel = panelRef.value
  if (!anchorEl || !panel) return

  const anchorRect = anchorEl.getBoundingClientRect()
  const viewportHeight = window.innerHeight

  // The cap comes off before measuring so the height read is the one the panel WANTS, not
  // the one a previous, shorter open left it clamped to. Written straight to the element -
  // a ref would reach the DOM only after these reads (see WorkspaceSubmenu's place()).
  panel.style.maxHeight = ''
  const height = panel.offsetHeight
  const available = viewportHeight - 2 * VIEWPORT_MARGIN
  panel.style.maxHeight = height > available ? `${available}px` : ''

  // Top-aligned to its button, lifted until it fits, and scrolling inside the viewport
  // when even that is not enough - this panel exists precisely in short windows.
  const effectiveHeight = Math.min(height, available)
  top.value = Math.max(
    VIEWPORT_MARGIN,
    Math.min(anchorRect.top, viewportHeight - effectiveHeight - VIEWPORT_MARGIN),
  )

  // Inward off the rail's sheet - the one side there is - and never over it.
  const clearEdge = props.keepClearOf?.getBoundingClientRect().left ?? anchorRect.left
  left.value = Math.max(VIEWPORT_MARGIN, clearEdge - KEEP_CLEAR_GAP - panel.offsetWidth)

  placed.value = true
}

// A resize can hand back the room the rail was missing, at which point the parent unmounts
// the "more" button and closes this panel - but until it does, the panel must stay placed
// against wherever its anchor moved to.
useEventListener(window, 'resize', () => props.open && place())

// The row count changes with the window height while the panel is open - a shrink feeds it
// more rows - so a re-place on the keys is a re-place on the panel's own height.
watch(
  () => [props.open, props.collapsedKeys.length],
  async ([isOpen]) => {
    if (!isOpen) {
      placed.value = false
      return
    }
    await nextTick()
    place()
    // Escape needs somewhere to land: the rail's buttons are tabindex="-1", so without
    // this the focus is still in a DOM tree this teleported panel is not part of. Always
    // safe to take - this menu only ever opens by click, never by a passing hover.
    panelRef.value?.focus({ preventScroll: true })
  },
)
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      ref="panelRef"
      class="overflow-menu"
      :style="{
        top: `${top}px`,
        left: `${left}px`,
        visibility: placed ? 'visible' : 'hidden',
      }"
      tabindex="-1"
      role="menu"
      @click.stop
      @keydown.escape.stop="close()"
    >
      <div
        v-for="row in rows"
        :key="row.key"
        class="menu-row"
        role="menuitem"
        :title="rowTitle(row)"
        @click="onRowClick(row)"
      >
        <span class="menu-icon">
          <component :is="row.icon" :style="row.color ? { color: row.color } : {}" />
        </span>
        <span class="menu-label">{{ row.label }}</span>
        <span v-if="row.shortcut" class="menu-shortcut">{{ row.shortcut }}</span>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
/* A floating sheet, so it is one that casts a shadow (in this app only floating panels
   do). Same surface, frame and z-index rationale as WorkspaceSubmenu: teleporting to the
   body puts it against the app's body-level layers, not the rail's own stack. */
.overflow-menu {
  position: fixed;
  z-index: 9999;
  display: flex;
  flex-direction: column;
  min-width: 160px;
  padding: 4px 0;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.18);
  overflow-y: auto;
  direction: rtl;
  outline: none;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

/* The hamburger menu's row, since these are its rows relocated. */
.menu-row {
  display: flex;
  align-items: center;
  gap: 10px;
  height: 32px;
  padding: 0 10px;
  cursor: pointer;
  flex-shrink: 0;
}
.menu-row:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.menu-row:active {
  background: color-mix(in srgb, var(--text-primary) 10%, transparent);
}

.menu-icon {
  display: flex;
  align-items: center;
  flex-shrink: 0;
}
.menu-icon svg {
  width: 18px;
  height: 18px;
}

.menu-label {
  font-size: 13px;
  color: var(--text-primary);
  white-space: nowrap;
  flex: 1;
}

.menu-shortcut {
  font-size: 11px;
  color: var(--text-secondary);
  white-space: nowrap;
  direction: ltr;
  margin-inline-start: auto;
}
</style>
