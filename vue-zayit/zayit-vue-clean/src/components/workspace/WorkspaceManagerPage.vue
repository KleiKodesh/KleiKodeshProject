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
import type { Workspace } from '@/utils/idbPersistence'

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
  if (id === wsStore.activeId) return
  await wsStore.switchWorkspace(id)
  window.location.reload()
}

async function confirmDelete(id: string) {
  await wsStore.deleteWorkspace(id)
  confirmDeleteId.value = null
  // If we deleted the active workspace, the store already switched — reload
  if (wsStore.activeId !== id) return
  window.location.reload()
}

function startConfirmDelete(id: string) {
  confirmDeleteId.value = id
  editingId.value = null
}
</script>

<template>
  <div class="ws-page">
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
            @keydown.escape="cancelEdit"
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
          <span class="ws-name confirm-text">למחוק את "{{ ws.name }}"?</span>
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
        placeholder="שם סביבת עבודה חדשה"
        @keydown.enter="create"
      />
      <button class="create-btn" :disabled="!newName.trim()" @click="create">
        <IconAdd20Regular />
        <span>צור</span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.ws-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--bg-primary);
  direction: rtl;
}

.ws-list {
  flex: 1;
  overflow-y: auto;
}

.ws-row {
  display: flex;
  align-items: center;
  height: 44px;
  padding: 0 12px;
  gap: 6px;
  border-bottom: 1px solid var(--border-color);
}
.ws-row:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.ws-row.active {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
  border-inline-start: 2px solid var(--accent-color);
  padding-inline-start: 10px;
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
  width: 28px;
  height: 28px;
  border-radius: 4px;
  font-size: 11px;
  padding: 0 6px;
}
.icon-btn svg {
  width: 16px;
  height: 16px;
}
.icon-btn.danger {
  color: #e53e3e;
}
.icon-btn.danger:hover {
  background: color-mix(in srgb, #e53e3e 12%, transparent);
}
.icon-btn:disabled {
  opacity: 0.3;
  pointer-events: none;
}

.ws-input-inline {
  flex: 1;
  height: 28px;
}

.ws-create {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  border-top: 1px solid var(--border-color);
  background: var(--bg-secondary);
  flex-shrink: 0;
}

.ws-input {
  flex: 1;
  height: 32px;
  padding: 0 10px;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  color: var(--text-primary);
  font-size: 13px;
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
  gap: 4px;
  height: 32px;
  padding: 0 12px;
  border-radius: 4px;
  font-size: 13px;
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
  width: 16px;
  height: 16px;
}
</style>
