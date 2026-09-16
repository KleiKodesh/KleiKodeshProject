<script setup lang="ts">
import { ref, watch, nextTick } from 'vue'
import {
  IconAdd20Regular,
  IconEdit20Regular,
  IconDelete20Regular,
  IconCheckmark20Regular,
  IconDismiss20Regular,
} from '@iconify-prerendered/vue-fluent'
import { useWorkspaceStore } from '@/stores/workspaceStore'
import type { Workspace } from '@/stores/workspaceStore'

/**
 * The whole of workspace management, as a menu panel rather than a page.
 *
 * It used to be a destination - a tab, a home tile, Ctrl+9 - for a list that is only
 * ever a few short rows and is picked from mid-task, so it is a menu now: a flyout off
 * the nav rail and a submenu inside the hamburger menu. Both surfaces render THIS
 * component, the way both render `appNavItems`, so the two can never drift.
 *
 * Every mutation lives in `workspaceStore`. What is here is the rows and the three bits
 * of row-local state: which row is being renamed, which is confirming a delete, and the
 * new-name box.
 *
 * The whole menu is **one keyboard list**, and that includes the new-name box at the
 * bottom: the rows and the box are stops on a single up/down walk, so arrowing past the
 * last row lands the caret in the box and arrowing up out of it returns to the last row.
 * Treating the box as a stop rather than as a separate tab target is the point - a reader
 * creating a workspace is doing the same job as one picking one, and should not have to
 * change input device or hunt for a tab stop in between.
 *
 * On a row: Enter switches to it, Delete starts its delete confirmation, F2 starts a
 * rename. In the box: Enter creates. Home/End jump to the ends of the walk.
 *
 * While a row is asking "delete this?", that question owns both answer keys for as long
 * as it stands: **Enter deletes, Escape backs out**. Neither reaches its usual meaning
 * until the question is gone, which is the whole point of a confirmation - it would be a
 * poor one if the key the reader just pressed to raise it also answered it.
 *
 * `useListKeys` is deliberately NOT used here, though the rows still carry its
 * `data-nav-item` marker and its `is-focused` class so they look like every other list in
 * the app. That composable binds its listener to the container it is given and assumes the
 * container is what holds the focus - but this menu never holds focus. The host panel does
 * (`WorkspaceSubmenu` focuses itself on open), and the panel is this menu's PARENT, so
 * keydowns there bubble up and away and never reach a listener bound down here. The walk
 * is therefore driven by `onPanelKeydown`, which the panel calls on the element the focus
 * is actually on. A text field that owns its own caret keys is the other reason: `Home`,
 * `End` and the arrows have to mean something different while the caret is in the box.
 *
 * No surface of its own - the panel that hosts it paints the background, frame and
 * shadow, so it drops into either host unchanged.
 */
const emit = defineEmits<{ close: [] }>()

const wsStore = useWorkspaceStore()

const newName = ref('')
const editingId = ref<string | null>(null)
const editingName = ref('')
const confirmDeleteId = ref<string | null>(null)

// ── Keyboard ────────────────────────────────────────────────────
//
// One walk over `rowCount + 1` stops: every workspace row, then the new-name box last.
// `focusedIndex === wsStore.workspaces.length` IS the box, which is why the bound below
// is `<=` the row count rather than `<` it.
const menuRef = ref<HTMLElement | null>(null)
const newNameInput = ref<HTMLInputElement | null>(null)

/** -1 until something is focused; `wsStore.workspaces.length` means the create box. */
const focusedIndex = ref(-1)

/** The index that means "the create box" - one past the last row. */
function createBoxIndex() {
  return wsStore.workspaces.length
}

function isCreateBoxFocused() {
  return focusedIndex.value === createBoxIndex()
}

// A row being renamed or confirming a delete has swapped its name for a text box or a pair
// of confirm buttons, and Enter there already means something else ("save the name",
// "yes, delete"). Walking onto such a row and pressing Enter must not ALSO switch away
// from it, so activation is ignored while the row is in either of those states.
function isRowEditing(id: string) {
  return editingId.value === id || confirmDeleteId.value === id
}

/** Put the walk on the row with this id, if it is still in the list. */
function syncFocusToRow(id: string) {
  const index = wsStore.workspaces.findIndex((ws) => ws.id === id)
  if (index >= 0) focusedIndex.value = index
}

/**
 * Move the walk to `index`, and move the real DOM focus with it.
 *
 * The create box is a genuine text field, so landing on it has to put the caret there -
 * a highlight alone would leave the reader typing into nothing. Leaving it has to hand
 * the focus back to the panel, or the caret stays in the box and every further arrow
 * press walks the text instead of the list.
 */
function moveTo(index: number) {
  const last = createBoxIndex()
  const clamped = Math.max(0, Math.min(last, index))
  focusedIndex.value = clamped

  if (clamped === last) {
    void nextTick(() => newNameInput.value?.focus())
    return
  }

  // Back onto a row: take the caret out of the box before scrolling, so the field does
  // not keep receiving keys meant for the walk. The panel is this menu's parent and is
  // what the host focused on open, so it is where the focus belongs between stops.
  if (document.activeElement === newNameInput.value) {
    menuRef.value?.parentElement?.focus({ preventScroll: true })
  }
  void nextTick(() => {
    const rows = menuRef.value?.querySelectorAll<HTMLElement>('[data-nav-item]')
    rows?.[clamped]?.scrollIntoView({ block: 'nearest' })
  })
}

/**
 * The menu's key handling, called by the host panel on the element that actually holds
 * the focus. Returns true when the key was consumed, so the panel knows to stop it.
 *
 * Only the keys the walk owns are claimed. Anything else - typing into the create box
 * included - is left alone, which is what keeps the box a normal text field.
 */
function onPanelKeydown(e: KeyboardEvent): boolean {
  const inBox = isCreateBoxFocused()

  // Escape backs out ONE level at a time. A row that is mid-rename or mid-delete-confirm
  // is a level of this menu's own, so Escape cancels that and the panel stays up; only
  // once there is no such row does Escape fall through to the host, whose Escape closes
  // the panel. Without this the panel's handler takes every Escape first (it is the
  // focused element, and the rename box never actually holds the focus), so cancelling a
  // rename shut the whole picker instead.
  if (e.code === 'Escape') {
    if (editingId.value !== null) {
      cancelEdit()
      return true
    }
    if (confirmDeleteId.value !== null) {
      confirmDeleteId.value = null
      return true
    }
    return false
  }

  switch (e.code) {
    case 'ArrowDown':
      moveTo(focusedIndex.value < 0 ? 0 : focusedIndex.value + 1)
      return true
    case 'ArrowUp':
      // From the box this walks back onto the last row; from row 0 it stays put rather
      // than wrapping, so the ends of the list are felt rather than looped past.
      moveTo(focusedIndex.value <= 0 ? 0 : focusedIndex.value - 1)
      return true
    case 'Home':
      // The caret keys win while the reader is typing: Home/End in a text field mean
      // "start/end of what I typed", and stealing them would be worse than not offering
      // the jump at all.
      if (inBox) return false
      moveTo(0)
      return true
    case 'End':
      if (inBox) return false
      moveTo(createBoxIndex())
      return true
    case 'Enter': {
      // A row asking "delete this?" has put a question on screen, and Enter is the answer
      // to THAT question for as long as it stands - not "switch to this workspace", which
      // is what Enter means on every other row. Checked before the create box and before
      // the row guard below, both of which would otherwise answer for it.
      if (confirmDeleteId.value !== null) {
        void confirmDelete(confirmDeleteId.value)
        return true
      }
      if (inBox) {
        void create()
        return true
      }
      const ws = wsStore.workspaces[focusedIndex.value]
      if (!ws || isRowEditing(ws.id)) return false
      void switchTo(ws.id)
      return true
    }
    case 'Delete': {
      if (inBox) return false
      const ws = wsStore.workspaces[focusedIndex.value]
      // Same guard the delete BUTTON carries: the last workspace cannot be deleted, or
      // there would be nothing left to switch to.
      if (!ws || isRowEditing(ws.id) || wsStore.workspaces.length <= 1) return false
      startConfirmDelete(ws.id)
      return true
    }
    case 'F2': {
      // The keyboard's way to the rename button, which is otherwise reachable only by
      // pointer. F2 is the rename key throughout Windows, so it needs no teaching.
      if (inBox) return false
      const ws = wsStore.workspaces[focusedIndex.value]
      if (!ws || isRowEditing(ws.id)) return false
      startEdit(ws)
      return true
    }
    default:
      return false
  }
}

// Open with the active workspace highlighted: it is the row the reader is oriented from,
// so one press of an arrow key moves to its neighbour rather than jumping to the top of
// the list.
//
// Watched on the LIST, not on `activeId` alone: deleting a workspace that is not the
// active one leaves `activeId` untouched while every index below the deleted row shifts
// up by one, which would leave the highlight pointing at the wrong row - or, for a
// deleted last row, past the end of the list entirely.
watch(
  () => wsStore.workspaces.map((ws) => ws.id).join('\u0000'),
  () => {
    // Never drag the caret out of the create box on a list change: creating a workspace
    // changes the list, and the reader may well be typing the next name.
    if (isCreateBoxFocused()) return
    const index = wsStore.workspaces.findIndex((ws) => ws.id === wsStore.activeId)
    focusedIndex.value = index >= 0 ? index : -1
  },
  { immediate: true },
)

defineExpose({ onPanelKeydown })

async function create() {
  const name = newName.value.trim()
  if (!name) return
  const ws = await wsStore.createWorkspace(name)
  newName.value = ''
  await switchTo(ws.id)
}

function startEdit(ws: Workspace) {
  editingId.value = ws.id
  editingName.value = ws.name
  confirmDeleteId.value = null
  // Keep the walk on this row, so cancelling the rename leaves the reader where they were
  // rather than back at nothing. Matters for the pointer path too: clicking the rename
  // button on a row should make that row the keyboard's row as well.
  syncFocusToRow(ws.id)
}

/**
 * Hand the focus back to the host panel after the rename box goes away.
 *
 * The box is `autofocus`ed and stops its own keys, so while it is up it owns the keyboard.
 * When it unmounts the focus would otherwise be left on a removed element - which the
 * browser resolves to `<body>`, where none of the walk's keys arrive. The reader would
 * finish a rename and find the arrows dead with no way back in but Escape and F8.
 *
 * `nextTick` because the box is still mounted on this tick: focusing the panel before Vue
 * has removed the input just lets the input take the focus straight back.
 */
function returnFocusToPanel() {
  void nextTick(() => {
    menuRef.value?.parentElement?.focus({ preventScroll: true })
  })
}

async function commitEdit() {
  if (!editingId.value) return
  await wsStore.renameWorkspace(editingId.value, editingName.value)
  editingId.value = null
  returnFocusToPanel()
}

function cancelEdit() {
  editingId.value = null
  returnFocusToPanel()
}

async function switchTo(id: string) {
  // Picking the workspace that is already active is just a dismissal - no reload.
  if (id === wsStore.activeId) {
    emit('close')
    return
  }
  await wsStore.switchWorkspace(id)
  window.location.reload()
}

async function confirmDelete(id: string) {
  // Captured BEFORE the delete: deleteWorkspace reassigns activeId, so asking afterwards
  // always answers "not the one we deleted" and the reload never ran - leaving the deleted
  // workspace's live tabs in memory under the surviving workspace's id, which persistTabs
  // then wrote straight over that workspace's saved tab list.
  const wasActive = wsStore.activeId === id
  await wsStore.deleteWorkspace(id)
  confirmDeleteId.value = null
  // If we deleted the active workspace, the store already switched - reload
  if (!wasActive) return
  window.location.reload()
}

function startConfirmDelete(id: string) {
  confirmDeleteId.value = id
  editingId.value = null
  syncFocusToRow(id)
}

/**
 * Keep the text fields' own typing out of the host panel - but only the typing.
 *
 * This used to be a blanket `stopPropagation` on every key but Escape, guarding against
 * the hamburger dropdown that used to host this menu and ran `useListKeys` on bubbled
 * keydowns. That host is gone; `WorkspaceSubmenu` is the only one now, and it is the panel
 * that drives the walk - by handling keydowns as they bubble UP through it from whatever
 * holds the focus. So a blanket stop here is actively wrong: it severs the create box from
 * the walk, and `ArrowUp` out of the box reaches nothing. That is a bug this shipped with
 * until the browser caught it.
 *
 * So the keys the WALK owns are let through to the panel, and everything else - the actual
 * typing - is stopped here. Escape is let through too: it is the host's "back out one
 * level", and this component does not own a level of its own to back out of.
 */
/**
 * The keys the host panel drives the walk with; a field must not swallow these.
 *
 * `Enter` is on the list because in the create box Enter IS a walk action - it creates the
 * workspace - and the panel is what runs it. Leaving it off meant the box stopped its own
 * Enter and nothing ever created anything.
 */
const WALK_KEYS = ['ArrowUp', 'ArrowDown', 'Home', 'End', 'Escape', 'Enter']


function onFieldKeydown(e: KeyboardEvent) {
  if (WALK_KEYS.includes(e.code)) return
  e.stopPropagation()
}
</script>

<template>
  <div ref="menuRef" class="ws-menu" @click.stop>
    <div class="ws-list">
      <div
        v-for="(ws, index) in wsStore.workspaces"
        :key="ws.id"
        class="ws-row"
        data-nav-item
        :class="{ active: ws.id === wsStore.activeId, 'is-focused': focusedIndex === index }"
      >
        <template v-if="editingId === ws.id">
          <input
            v-model="editingName"
            name="workspace-name-edit"
            class="ws-input ws-input-inline"
            @keydown.stop
            @keydown.enter="commitEdit"
            @keydown.escape.stop="cancelEdit"
            autofocus
          />
          <button class="icon-btn" title="שמור" @click="commitEdit">
            <IconCheckmark20Regular />
          </button>
          <button class="icon-btn" title="ביטול" @click="cancelEdit">
            <IconDismiss20Regular />
          </button>
        </template>
        <template v-else-if="confirmDeleteId === ws.id">
          <span class="ws-name confirm-text">{{ ws.name }} — למחוק?</span>
          <button class="icon-btn danger" @click="confirmDelete(ws.id)">מחק</button>
          <button class="icon-btn" @click="confirmDeleteId = null">ביטול</button>
        </template>
        <template v-else>
          <span class="ws-name" @click="switchTo(ws.id)">{{ ws.name }}</span>
          <span v-if="ws.id === wsStore.activeId" class="active-badge">פעיל</span>
          <div class="ws-actions">
            <button class="icon-btn" title="שנה שם" @click.stop="startEdit(ws)">
              <IconEdit20Regular />
            </button>
            <button
              class="icon-btn danger"
              title="מחק"
              :disabled="wsStore.workspaces.length <= 1"
              @click.stop="startConfirmDelete(ws.id)"
            >
              <IconDelete20Regular />
            </button>
          </div>
        </template>
      </div>
    </div>

    <div class="ws-create">
      <input
        ref="newNameInput"
        v-model="newName"
        name="workspace-name-new"
        class="ws-input"
        :class="{ 'is-focused': isCreateBoxFocused() }"
        placeholder="סביבת עבודה חדשה"
        @keydown="onFieldKeydown"
        @focus="focusedIndex = createBoxIndex()"
      />
      <button class="create-btn" :disabled="!newName.trim()" title="צור" @click="create">
        <IconAdd20Regular />
      </button>
    </div>
  </div>
</template>

<style scoped>
/* No height of its own: the panel that hosts it clamps to the viewport (WorkspaceSubmenu),
   and this menu takes whatever that leaves. `min-height: 0` is what lets the list below
   actually shrink and scroll inside a flex column instead of overflowing it, which is how
   the create box stays reachable in a short window. */
.ws-menu {
  display: flex;
  flex-direction: column;
  min-width: 200px;
  min-height: 0;
  direction: rtl;
}

.ws-list {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

/* 32px rows, matching the menu rows of the surfaces that host this - the panel is a
   continuation of that menu, not a panel dropped inside one. */
.ws-row {
  display: flex;
  align-items: center;
  height: 32px;
  padding: 0 10px;
  gap: 4px;
}
.ws-row:hover,
.ws-row.is-focused {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.ws-row.active .ws-name {
  color: var(--accent-color);
  font-weight: 500;
}

.ws-name {
  flex: 1;
  font-size: 13px;
  color: var(--text-primary);
  cursor: pointer;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.confirm-text {
  flex: 1;
  font-size: 12px;
  color: var(--text-secondary);
  cursor: default;
}

.active-badge {
  font-size: 10px;
  color: var(--accent-color);
  border: 1px solid color-mix(in srgb, var(--accent-color) 50%, transparent);
  border-radius: 4px;
  padding: 1px 5px;
  flex-shrink: 0;
}

/* Revealed on row hover, like the rest of the app's list rows: two more glyphs on every
   row at rest would read as four columns of controls rather than a list of names. */
.ws-actions {
  display: flex;
  gap: 2px;
  opacity: 0;
  transition: opacity 100ms;
}
/* The keyboard-focused row shows its actions for the same reason the hovered one does:
   it is the row the reader is on, and rename/delete have to be visible to be reachable
   from there. */
.ws-row:hover .ws-actions,
.ws-row.is-focused .ws-actions {
  opacity: 1;
}

.icon-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  min-width: 24px;
  height: 24px;
  border-radius: 4px;
  font-size: 11px;
  padding: 0 5px;
}
.icon-btn svg {
  width: 15px;
  height: 15px;
}
.icon-btn.danger {
  color: var(--status-danger);
}
.icon-btn.danger:hover {
  background: color-mix(in srgb, var(--status-danger) 12%, transparent);
}
.icon-btn:disabled {
  opacity: 0.3;
  pointer-events: none;
}

.ws-input-inline {
  flex: 1;
  height: 24px;
  font-size: 12px;
}

.ws-create {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 8px;
  border-top: 1px solid var(--border-color);
  flex-shrink: 0;
}

.ws-input {
  flex: 1;
  min-width: 0;
  height: 26px;
  padding: 0 8px;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  color: var(--text-primary);
  font-size: 12px;
  outline: none;
}
.ws-input:focus {
  border-color: var(--accent-color);
}
.ws-input::placeholder {
  color: var(--text-secondary);
}

.create-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  border-radius: 4px;
  color: var(--accent-color);
  border: 1px solid color-mix(in srgb, var(--accent-color) 40%, transparent);
  background: color-mix(in srgb, var(--accent-color) 8%, transparent);
  flex-shrink: 0;
}
.create-btn:hover {
  background: color-mix(in srgb, var(--accent-color) 16%, transparent);
}
.create-btn:disabled {
  opacity: 0.4;
  pointer-events: none;
}
.create-btn svg {
  width: 15px;
  height: 15px;
}
</style>
