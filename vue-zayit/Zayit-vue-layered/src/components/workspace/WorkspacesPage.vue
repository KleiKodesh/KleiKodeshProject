<template>
  <div class="page-container">
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
                class="btn-primary">
          <Icon icon="fluent:add-24-regular" />
          צור
        </button>
      </div>

      <div class="workspace-list">
        <div v-for="workspaceId in workspaces"
             :key="workspaceId"
             class="workspace-item"
             :class="{ 'active': workspaceId === currentWorkspace }"
             @click="switchTo(workspaceId)">

          <div v-if="editingId === workspaceId"
               class="workspace-edit"
               @click.stop>
            <input v-model="editingName"
                   @keydown.enter="saveEdit"
                   @keydown.esc="cancelEdit"
                   ref="editInput"
                   class="workspace-name-input small"
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
               class="workspace-info">
            <div class="workspace-name">{{ getWorkspaceName(workspaceId) }}</div>
            <div class="workspace-meta">{{ getWorkspaceTabCount(workspaceId) }} פריטים</div>

            <div class="workspace-actions"
                 @click.stop>
              <button @click="startEdit(workspaceId)"
                      class="btn-icon"
                      title="שנה שם">
                <Icon icon="fluent:edit-24-regular" />
              </button>
              <button v-if="workspaceId !== 'default'"
                      @click="deleteWorkspaceHandler(workspaceId)"
                      class="btn-icon btn-danger"
                      title="מחק">
                <Icon icon="fluent:delete-24-regular" />
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { Icon } from '@iconify/vue';
import { useWorkspaceEditor } from '@/components/workspace/useWorkspaceEditor';

const {
  workspaces,
  currentWorkspace,
  newWorkspaceName,
  editingId,
  editingName,
  editInput,
  getWorkspaceTabCount,
  getWorkspaceName,
  createNew,
  startEdit,
  saveEdit,
  cancelEdit,
  switchTo,
  deleteWorkspaceHandler
} = useWorkspaceEditor();
</script>

<style scoped>
.page-container {
  height: 100%;
  overflow-y: auto;
  background: var(--bg-primary);
}

.workspace-manager {
  max-width: 600px;
  margin: 0 auto;
  padding: 20px 16px;
  direction: rtl;
}

.workspace-header {
  margin-bottom: 20px;
  text-align: center;
}

.workspace-header h2 {
  font-size: 22px;
  font-weight: 600;
  margin: 0;
  color: var(--text-primary);
}

.workspace-create {
  display: flex;
  gap: 8px;
  margin-bottom: 20px;
  padding: 12px;
  background: var(--bg-secondary);
  border: 2px dashed var(--border-color);
  border-radius: 8px;
}

.workspace-name-input {
  flex: 1;
  padding: 8px 12px;
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

.workspace-name-input.small {
  padding: 6px 8px;
  font-size: 13px;
}

.btn-primary {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 16px;
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
  background: var(--accent-color);
  filter: brightness(1.1);
}

.btn-primary:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.workspace-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.workspace-item {
  display: flex;
  align-items: center;
  padding: 10px 12px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  transition: all 0.2s ease;
  cursor: pointer;
}

.workspace-item.active {
  border-color: var(--accent-color);
  background: var(--accent-bg-light);
  box-shadow: 0 0 0 1px var(--accent-color);
}

.workspace-item:hover:not(.active) {
  background: var(--hover-bg);
}

.workspace-edit {
  display: flex;
  align-items: center;
  gap: 6px;
  width: 100%;
}

.workspace-info {
  display: flex;
  align-items: center;
  width: 100%;
  gap: 12px;
}

.workspace-name {
  font-size: 15px;
  font-weight: 500;
  color: var(--text-primary);
  flex: 1;
}

.workspace-meta {
  font-size: 12px;
  color: var(--text-secondary);
}

.workspace-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-right: auto;
}

.btn-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  padding: 0;
  background: transparent;
  color: var(--text-primary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
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
</style>
