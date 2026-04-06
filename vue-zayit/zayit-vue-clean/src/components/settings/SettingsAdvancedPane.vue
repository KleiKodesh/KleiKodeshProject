<script setup lang="ts">
import { ref, nextTick } from 'vue'
import { IconFolderOpen20Regular } from '@iconify-prerendered/vue-fluent'
import { useSettingsPage } from './useSettingsPage'
import { useSettingsStore } from '@/stores/settingsStore'
import ConfirmDialog from '@/components/common/ConfirmDialog.vue'
import { resetting } from '@/utils/resetState'
import { isHosted, onDbReady } from '@/host/db'

const { resetSettings, resetAll } = useSettingsPage()
const settings = useSettingsStore()

const dbPath = ref(window.__webviewDbPath ?? '')
const editingPath = ref(false)
const pathInputRef = ref<HTMLInputElement | null>(null)

function pickDbPath() {
  window.__webviewPickDbPath?.()
}

function startEditing() {
  editingPath.value = true
  nextTick(() => pathInputRef.value?.focus())
}

async function commitPath() {
  editingPath.value = false
  if (!window.__webviewSetDbPath) return
  try {
    await window.__webviewSetDbPath(dbPath.value)
    onDbReady(dbPath.value)
  } catch {
    dbPath.value = window.__webviewDbPath ?? ''
  }
}

type ConfirmAction = { label: string; desc: string; action: () => Promise<void> | void }
const pendingConfirm = ref<ConfirmAction | null>(null)

function confirmAction(action: ConfirmAction) {
  pendingConfirm.value = action
}

async function runConfirmed() {
  if (!pendingConfirm.value) return
  const action = pendingConfirm.value
  pendingConfirm.value = null
  await action.action()
}

function cancelConfirm() {
  pendingConfirm.value = null
}

function resetSettingsAndReload() {
  resetting.value = true
  resetSettings()
  settings.completeSetup()
  window.location.reload()
}

function confirmResetAll() {
  confirmAction({
    label: 'איפוס האפליקציה',
    desc: 'פעולה זו תמחק את כל נתוני האפליקציה ואינדקס החיפוש ותטען אותה מחדש. לא ניתן לבטל פעולה זו.',
    action: () => {
      resetting.value = true
      resetAll()
    },
  })
}

function confirmResetSettings() {
  confirmAction({
    label: 'איפוס ההגדרות',
    desc: 'פעולה זו תאפס את הגדרות התצוגה והקריאה לברירות המחדל. מסד הנתונים והיסטוריית הקריאה לא יושפעו.',
    action: resetSettingsAndReload,
  })
}
</script>

<template>
  <div class="advanced-pane">
    <template v-if="isHosted">
      <div class="db-path-row">
        <span class="db-path-label">נתיב מסד הנתונים</span>
        <div class="db-path-field" :class="{ editing: editingPath }">
          <button class="folder-btn" @click="pickDbPath" title="בחר קובץ">
            <IconFolderOpen20Regular />
          </button>
          <input
            v-if="editingPath"
            ref="pathInputRef"
            v-model="dbPath"
            name="db-path"
            class="db-path-input"
            dir="ltr"
            @blur="commitPath"
            @keydown.enter="commitPath"
            @keydown.escape="editingPath = false"
          />
          <span v-else class="db-path-text" :class="{ placeholder: !dbPath }" @click="startEditing">
            {{ dbPath || 'לא נבחר נתיב' }}
          </span>
        </div>
      </div>
    </template>

    <p class="reset-desc">
      מאפס רק את הגדרות התצוגה והקריאה לברירות המחדל. מסד הנתונים, היסטוריית הקריאה, והטאבים הפתוחים
      נשמרים.
    </p>
    <button class="reset-all-btn" @click="confirmResetSettings">איפוס ההגדרות</button>
    <p class="reset-desc">
      מוחק את כל נתוני האפליקציה — הגדרות, היסטוריית קריאה, מיקומי גלילה, טאבים פתוחים, ואינדקס
      החיפוש. לא ניתן לבטל פעולה זו.
    </p>
    <button class="reset-all-btn" @click="confirmResetAll">איפוס האפליקציה</button>

    <ConfirmDialog
      v-if="pendingConfirm"
      :title="pendingConfirm.label"
      :desc="pendingConfirm.desc"
      @confirm="runConfirmed"
      @cancel="cancelConfirm"
    />
  </div>
</template>

<style scoped>
.advanced-pane {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 12px;
}

.reset-desc {
  font-size: 12px;
  color: var(--text-secondary);
  line-height: 1.5;
  margin: 0;
}
.reset-all-btn {
  width: 140px;
  height: 32px;
  font-size: 13px;
  color: #e53e3e;
  border: 1px solid color-mix(in srgb, #e53e3e 40%, transparent);
  background: color-mix(in srgb, #e53e3e 8%, transparent);
}
.reset-all-btn:hover {
  background: color-mix(in srgb, #e53e3e 16%, transparent);
}

.db-path-row {
  display: flex;
  flex-direction: column;
  gap: 6px;
  width: 100%;
}
.db-path-label {
  font-size: 11px;
  color: var(--text-secondary);
}
.db-path-field {
  display: flex;
  align-items: center;
  height: 32px;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-secondary);
  overflow: hidden;
  transition: border-color 0.1s;
}
.db-path-field:hover {
  border-color: color-mix(in srgb, var(--text-secondary) 50%, transparent);
}
.db-path-field.editing {
  border-color: var(--accent-color);
}
.db-path-text {
  flex: 1;
  padding: 0 8px;
  font-size: 11px;
  color: var(--text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  direction: ltr;
  text-align: left;
  cursor: text;
  min-width: 0;
}
.db-path-text:hover {
  color: var(--text-primary);
}
.db-path-text.placeholder {
  direction: rtl;
  text-align: right;
  opacity: 0.6;
}
.db-path-input {
  flex: 1;
  height: 100%;
  padding: 0 8px;
  font-size: 11px;
  direction: ltr;
  text-align: left;
  background: transparent;
  border: none;
  outline: none;
  color: var(--text-primary);
  min-width: 0;
}
.folder-btn {
  flex-shrink: 0;
  width: 32px;
  height: 32px;
  padding: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  border: none;
  border-inline-end: 1px solid var(--border-color);
  border-radius: 0;
  background: transparent;
  color: var(--text-secondary);
}
.folder-btn:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
</style>
