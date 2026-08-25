<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import type { Component, ComponentPublicInstance } from 'vue'
import { useEventListener } from '@vueuse/core'
import {
  IconChevronLeft20Regular,
  IconOpen28Regular,
  IconSplitVertical20Filled,
  IconSplitVertical20Regular,
} from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { documentIcon } from '@/utils/documentIcons'
import { useBookViewStore } from '@/stores/bookViewStore'
import WorkspaceSubmenu from './WorkspaceSubmenu.vue'
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
 * label, icon, shortcut, read off the same tables the rail's buttons read. The one row
 * with behaviour of its own is workspaces, which is no action at all: like its row in the
 * hamburger menu it opens the picker (WorkspaceSubmenu) beside this menu, and that must
 * be wired here because the picker hangs off the row element, which only this menu has.
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

/** One collapsed button as a row. Only workspaces is not an action: it opens the picker. */
interface OverflowRow {
  key: string
  label: string
  icon: Component
  color?: string
  shortcut?: string
  isWorkspaces?: boolean
}

const workspacesIcon = documentIcon('apps')
const bookViewStore = useBookViewStore()

const rows = computed<OverflowRow[]>(() => props.collapsedKeys.map(overflowRow))

// The same face the rail's buttons wear - the split-view row is the one that changes
// with state, so its label and icon read the store here just as the rail button's do.
function overflowRow(key: string): OverflowRow {
  if (key === 'workspaces') {
    return {
      key,
      label: 'סביבות עבודה',
      icon: workspacesIcon.icon24,
      color: workspacesIcon.color || undefined,
      isWorkspaces: true,
    }
  }
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

// ── The workspaces picker off its row ─────────────────────────────────────────
const workspacesOpen = ref(false)
const workspacesRowEl = ref<HTMLElement | null>(null)
const workspacesSubmenu = ref<InstanceType<typeof WorkspaceSubmenu> | null>(null)
/**
 * The picker is teleported, so this menu's own outside-click watcher sees clicks inside
 * it as landing outside - the `ignore` names the panel element the picker exposes, same
 * as AppTitleBarNavDropdown does for its workspaces row.
 */
const workspacesPanelEl = computed<HTMLElement | null>(
  () => workspacesSubmenu.value?.panelEl ?? null,
)

// The workspaces row is one branch of the v-for, so there is no static ref to name - the
// element is picked out of the loop by a function ref.
function setRowElement(row: OverflowRow, el: Element | ComponentPublicInstance | null) {
  if (row.isWorkspaces) workspacesRowEl.value = el as HTMLElement | null
}

// A grow can hand the workspaces button back to the rail while this menu is still open -
// the picker must not be left floating beside a row that no longer exists.
//
// `pre`, deliberately: this has to close the picker BEFORE the patch that removes the row
// nulls the ref behind it. A post-flush watcher runs after that patch, leaving the picker
// up for a flush with a null anchor - place() early-returns on one, so it would hang
// frozen at its last coordinates beside a row already gone.
watch(
  () => props.collapsedKeys.includes('workspaces'),
  (isCollapsed) => {
    if (!isCollapsed) workspacesOpen.value = false
  },
  { flush: 'pre' },
)

// The anchor is passed as the toggle so the composable leaves a click on it to the
// button's own @click, instead of closing on pointerdown and reopening on click.
useDropdownClose(panelRef, () => close(), {
  toggleButton: computed(() => props.anchor),
  ignore: [workspacesPanelEl],
  enabled: () => props.open,
})

function close() {
  workspacesOpen.value = false
  emit('update:open', false)
}

/** Escape backs out one level at a time: the open picker first, then this menu. */
function onEscape() {
  if (workspacesOpen.value) {
    workspacesOpen.value = false
    return
  }
  close()
}

function onRowClick(row: OverflowRow) {
  if (row.isWorkspaces) {
    workspacesSubmenu.value?.toggle()
    return
  }
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
      @keydown.escape.stop="onEscape()"
    >
      <div
        v-for="row in rows"
        :key="row.key"
        :ref="(el) => setRowElement(row, el)"
        class="menu-row"
        :class="{ 'menu-row--open': row.isWorkspaces && workspacesOpen }"
        role="menuitem"
        :title="rowTitle(row)"
        :aria-expanded="row.isWorkspaces ? workspacesOpen : undefined"
        @click="onRowClick(row)"
      >
        <span class="menu-icon">
          <component :is="row.icon" :style="row.color ? { color: row.color } : {}" />
        </span>
        <span class="menu-label">{{ row.label }}</span>
        <!-- Points the way the picker opens: inward, off this menu's edge. -->
        <span v-if="row.isWorkspaces" class="menu-submenu-chevron">
          <IconChevronLeft20Regular />
        </span>
        <span v-else-if="row.shortcut" class="menu-shortcut">{{ row.shortcut }}</span>
      </div>
      <!-- Inside the v-if subtree on purpose: closing this menu unmounts the picker too. -->
      <WorkspaceSubmenu
        ref="workspacesSubmenu"
        v-model:open="workspacesOpen"
        :anchor="workspacesRowEl"
        :keep-clear-of="panelRef"
        prefer="left"
        @close="panelRef?.focus()"
      />
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

/* Held while the picker is up, so the row keeps saying which panel is open. */
.menu-row--open {
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

.menu-submenu-chevron {
  display: flex;
  align-items: center;
  color: var(--text-secondary);
  margin-inline-start: auto;
  flex-shrink: 0;
}
.menu-submenu-chevron svg {
  width: 14px;
  height: 14px;
  color: inherit;
}
</style>
