<script setup lang="ts">
import { ref } from 'vue'
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
 * No surface of its own - the panel that hosts it paints the background, frame and
 * shadow, so it drops into either host unchanged.
 */
const emit = defineEmits<{ close: [] }>()

const wsStore = useWorkspaceStore()

const newName = ref('')
const editingId = ref<string | null>(null)
const editingName = ref('')
const confirmDeleteId = ref<string | null>(null)

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
}

async function commitEdit() {
  if (!editingId.value) return
  await wsStore.renameWorkspace(editingId.value, editingName.value)
  editingId.value = null
}

function cancelEdit() {
  editingId.value = null
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
}

/**
 * Keys typed in here are this menu's own and must not reach the host menu.
 *
 * The hamburger dropdown runs `useListKeys` on its root, which listens for bubbled
 * keydowns: without this, Enter in the new-name box would be swallowed and re-activate
 * whichever row the arrow keys had last focused, and the arrow keys would walk that
 * menu's rows while the caret sat in a text field.
 *
 * Escape is deliberately let through: it is the host's "back out one level", and this
 * component does not own a level of its own to back out of.
 */
function onKeydown(e: KeyboardEvent) {
  if (e.code === 'Escape') return
  e.stopPropagation()
}
</script>

<template>
  <div class="ws-menu" @click.stop @keydown="onKeydown">
    <div class="ws-list">
      <div
        v-for="ws in wsStore.workspaces"
        :key="ws.id"
        class="ws-row"
        :class="{ active: ws.id === wsStore.activeId }"
      >
        <template v-if="editingId === ws.id">
          <input
            v-model="editingName"
            name="workspace-name-edit"
            class="ws-input ws-input-inline"
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
        v-model="newName"
        name="workspace-name-new"
        class="ws-input"
        placeholder="סביבת עבודה חדשה"
        @keydown.enter="create"
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
.ws-row:hover {
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
.ws-row:hover .ws-actions {
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
