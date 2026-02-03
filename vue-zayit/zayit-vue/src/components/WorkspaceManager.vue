<template>
  <div class="workspace-manager">
    <div class="workspace-header">
      <h2>ניהול סביבות עבודה</h2>
    </div>

    <div class="workspace-create">
      <input v-model="newWorkspaceName"
             @keydown.enter="createNew"
             placeholder="שם סביבת עבודה חדשה"
             class="workspace-name-input"
             type="text" />
      <button @click="createNew"
              :disabled="!newWorkspaceName.trim()"
              class="btn-primary btn-icon-only"
              title="צור סביבת עבודה חדשה">
        <Icon icon="fluent:add-24-regular" />
      </button>
    </div>

    <div class="workspace-list">
      <div v-for="workspace in sortedWorkspaces"
           :key="workspace.id"
           class="workspace-item"
           :class="{ 'active': workspace.id === workspaceStore.currentWorkspaceId }"
           @click="switchTo(workspace.id)">
        <div class="workspace-info">
          <div v-if="editingId === workspace.id"
               class="workspace-edit"
               @click.stop>
            <input v-model="editingName"
                   @keydown.enter="saveEdit"
                   @keydown.esc="cancelEdit"
                   ref="editInput"
                   class="workspace-name-input"
                   type="text" />
            <button @click="saveEdit"
                    class="btn-icon"
                    title="שמור">
              <Icon icon="fluent:checkmark-24-regular" />
            </button>
            <button @click="cancelEdit"
                    class="btn-icon"
                    title="בטל">
              <Icon icon="fluent:dismiss-24-regular" />
            </button>
          </div>
          <div v-else
               class="workspace-details">
            <h3 class="workspace-name">{{ workspace.name }}</h3>
            <div class="workspace-meta">
              <span v-if="workspace.id === workspaceStore.currentWorkspaceId"
                    class="current-badge">פעיל</span>
              <span class="workspace-date">{{ formatDate(workspace.lastAccessedAt) }}</span>
            </div>
          </div>
        </div>

        <div class="workspace-actions"
             @click.stop>
          <button @click="startEdit(workspace)"
                  class="btn-icon"
                  title="שנה שם">
            <Icon icon="fluent:edit-24-regular" />
          </button>
          <button v-if="workspace.id !== 'default'"
                  @click="confirmDelete(workspace)"
                  class="btn-icon btn-danger"
                  title="מחק">
            <Icon icon="fluent:delete-24-regular" />
          </button>
        </div>
      </div>
    </div>

    <!-- Delete confirmation dialog -->
    <div v-if="deleteConfirm"
         class="modal-overlay"
         @click.self="cancelDelete">
      <div class="modal-content">
        <h3>מחיקת סביבת עבודה</h3>
        <p>האם אתה בטוח שברצונך למחוק את סביבת העבודה "{{ deleteConfirm.name }}"?</p>
        <p class="warning">כל הטאבים והנתונים בסביבה זו יימחקו לצמיתות.</p>
        <div class="modal-actions">
          <button @click="executeDelete"
                  class="btn-danger">
            מחק
          </button>
          <button @click="cancelDelete"
                  class="btn-secondary">
            בטל
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, nextTick } from 'vue';
import { Icon } from '@iconify/vue';
import { useWorkspaceStore, type Workspace } from '../stores/workspaceStore';
import { useTabStore } from '../stores/tabStore';

const workspaceStore = useWorkspaceStore();
const tabStore = useTabStore();

const newWorkspaceName = ref('');
const editingId = ref<string | null>(null);
const editingName = ref('');
const editInput = ref<HTMLInputElement>();
const deleteConfirm = ref<Workspace | null>(null);

const sortedWorkspaces = computed(() => {
  return [...workspaceStore.workspaces].sort((a, b) => {
    // Default workspace always first
    if (a.id === 'default') return -1;
    if (b.id === 'default') return 1;
    // Then by last accessed (most recent first)
    return b.lastAccessedAt - a.lastAccessedAt;
  });
});

const formatDate = (timestamp: number): string => {
  const date = new Date(timestamp);
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffMins = Math.floor(diffMs / 60000);
  const diffHours = Math.floor(diffMs / 3600000);
  const diffDays = Math.floor(diffMs / 86400000);

  if (diffMins < 1) return 'עכשיו';
  if (diffMins < 60) return `לפני ${diffMins} דקות`;
  if (diffHours < 24) return `לפני ${diffHours} שעות`;
  if (diffDays < 7) return `לפני ${diffDays} ימים`;

  return date.toLocaleDateString('he-IL', {
    year: 'numeric',
    month: 'short',
    day: 'numeric'
  });
};

const createNew = () => {
  const name = newWorkspaceName.value.trim();
  if (!name) return;

  const newId = workspaceStore.createWorkspace(name);
  newWorkspaceName.value = '';

  // Optionally switch to the new workspace
  // workspaceStore.switchWorkspace(newId);
};

const startEdit = (workspace: Workspace) => {
  editingId.value = workspace.id;
  editingName.value = workspace.name;
  nextTick(() => {
    editInput.value?.focus();
    editInput.value?.select();
  });
};

const saveEdit = () => {
  if (editingId.value && editingName.value.trim()) {
    workspaceStore.renameWorkspace(editingId.value, editingName.value.trim());
  }
  cancelEdit();
};

const cancelEdit = () => {
  editingId.value = null;
  editingName.value = '';
};

const switchTo = (workspaceId: string) => {
  // Just switch workspace, stay on workspace manager page
  workspaceStore.switchWorkspace(workspaceId);
};

const confirmDelete = (workspace: Workspace) => {
  deleteConfirm.value = workspace;
};

const executeDelete = () => {
  if (deleteConfirm.value) {
    workspaceStore.deleteWorkspace(deleteConfirm.value.id);
  }
  cancelDelete();
};

const cancelDelete = () => {
  deleteConfirm.value = null;
};
</script>

<style scoped>
.workspace-manager {
  max-width: 700px;
  margin: 0 auto;
  padding: 20px 16px;
  direction: rtl;
}

.workspace-header {
  margin-bottom: 16px;
}

.workspace-header h2 {
  font-size: 22px;
  font-weight: 600;
  margin: 0;
  color: var(--text-primary);
}

.workspace-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-bottom: 16px;
}

.workspace-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  transition: all 0.2s ease;
  cursor: pointer;
}

.workspace-item.active {
  border-color: var(--accent-color);
  background: var(--bg-tertiary);
}

.workspace-item:hover {
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
  transform: translateY(-1px);
}

.workspace-item.active:hover {
  transform: none;
  cursor: default;
}

.workspace-info {
  flex: 1;
  min-width: 0;
}

.workspace-edit {
  display: flex;
  align-items: center;
  gap: 6px;
}

.workspace-details {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.workspace-name {
  font-size: 16px;
  font-weight: 500;
  margin: 0;
  color: var(--text-primary);
}

.workspace-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
  color: var(--text-secondary);
}

.current-badge {
  display: inline-block;
  padding: 1px 6px;
  background: var(--accent-color);
  color: white;
  border-radius: 3px;
  font-size: 11px;
  font-weight: 500;
}

.workspace-actions {
  display: flex;
  align-items: center;
  gap: 6px;
}

.workspace-create {
  display: flex;
  gap: 8px;
  padding: 12px;
  background: var(--bg-secondary);
  border: 2px dashed var(--border-color);
  border-radius: 6px;
  margin-bottom: 16px;
}

.workspace-name-input {
  flex: 1;
  padding: 10px 12px;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-primary);
  color: var(--text-primary);
  font-size: 14px;
  outline: none;
  transition: border-color 0.2s ease;
}

.workspace-name-input:focus {
  border-color: var(--accent-color);
}

.btn-primary {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 10px 16px;
  background: var(--accent-color);
  color: white;
  border: none;
  border-radius: 6px;
  font-size: 14px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s ease;
  white-space: nowrap;
}

.btn-primary:hover:not(:disabled) {
  background: var(--accent-hover);
}

.btn-primary:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-icon-only {
  height: 38px;
  width: 38px;
  min-width: 38px;
  padding: 0;
  flex-shrink: 0;
}

.btn-secondary {
  padding: 10px 16px;
  background: var(--bg-secondary);
  color: var(--text-primary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  font-size: 14px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s ease;
}

.btn-secondary:hover {
  background: var(--hover-bg);
}

.btn-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  min-width: 32px;
  width: 32px;
  height: 32px;
  padding: 0;
  background: transparent;
  color: var(--text-primary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.2s ease;
  flex-shrink: 0;
}

.btn-icon:hover {
  background: var(--hover-bg);
}

.btn-danger {
  color: #ef4444;
  border-color: #ef4444;
}

.btn-danger:hover {
  background: rgba(239, 68, 68, 0.1);
}

/* Modal styles */
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 10000;
}

.modal-content {
  background: var(--bg-primary);
  border-radius: 12px;
  padding: 24px;
  max-width: 400px;
  width: 90%;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.2);
}

.modal-content h3 {
  margin: 0 0 16px 0;
  font-size: 20px;
  color: var(--text-primary);
}

.modal-content p {
  margin: 0 0 12px 0;
  color: var(--text-secondary);
  line-height: 1.5;
}

.modal-content .warning {
  color: #ef4444;
  font-weight: 500;
}

.modal-actions {
  display: flex;
  gap: 12px;
  margin-top: 24px;
  justify-content: flex-end;
}

.modal-actions button {
  min-width: 70px;
}

.modal-actions .btn-danger {
  background: #ef4444;
  color: white;
  border: none;
  padding: 8px 16px;
  white-space: nowrap;
  font-size: 14px;
}

.modal-actions .btn-danger:hover {
  background: #dc2626;
}

.modal-actions .btn-secondary {
  white-space: nowrap;
  font-size: 14px;
  padding: 8px 16px;
}
</style>
