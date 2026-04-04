<script setup lang="ts">
import { ref, nextTick } from 'vue'
import { IconFolderOpen20Regular } from '@iconify-prerendered/vue-fluent'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import { isHosted, onDbReady } from '@/host/db'
import SettingRow from './SettingRow.vue'
import SliderSetting from './SliderSetting.vue'
import ToggleGroup from './ToggleGroup.vue'
import ThemePicker from './ThemePicker.vue'

const settings = useSettingsStore()
const { censorDivineNames, appZoom, newTabPage, resumeLastRead } = storeToRefs(settings)

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
</script>

<template>
  <div class="general-pane">
    <SettingRow label="ערכת נושא">
      <ThemePicker />
    </SettingRow>

    <SliderSetting label="גודל תצוגה" v-model="appZoom" :min="0.5" :max="1.5" :step="0.05" />

    <SettingRow label="כיסוי שם ה'">
      <ToggleGroup
        v-model="censorDivineNames"
        :options="[
          { label: 'כתיב מלא', value: false },
          { label: 'כיסוי (ה←ק)', value: true },
        ]"
      />
    </SettingRow>

    <SettingRow label="פתח טאב חדש אל" wrap>
      <ToggleGroup
        v-model="newTabPage"
        :options="[
          { label: 'דף הבית', value: 'homepage' },
          { label: 'פתיחת ספר', value: 'openfile' },
          { label: 'היברו בוקס', value: 'hebrewbooks' },
          { label: 'חיפוש', value: 'kezayit-search' },
        ]"
      />
    </SettingRow>

    <SettingRow
      label="זכור מיקום אחרון בספר"
      title="בפתיחת ספר מחדש, האפליקציה תחזור אוטומטית למקום שבו הפסקת לקרוא"
    >
      <ToggleGroup
        v-model="resumeLastRead"
        :options="[
          { label: 'כן', value: true },
          { label: 'לא', value: false },
        ]"
      />
    </SettingRow>

    <template v-if="isHosted">
      <div class="db-path-row">
        <span class="db-path-label">נתיב מסד הנתונים</span>
        <div class="db-path-field" :class="{ editing: editingPath }">
          <input
            v-if="editingPath"
            ref="pathInputRef"
            v-model="dbPath"
            class="db-path-input"
            dir="ltr"
            @blur="commitPath"
            @keydown.enter="commitPath"
            @keydown.escape="editingPath = false"
          />
          <span v-else class="db-path-text" :class="{ placeholder: !dbPath }" @click="startEditing">
            {{ dbPath || 'לא נבחר נתיב' }}
          </span>
          <button class="folder-btn" @click="pickDbPath" title="בחר קובץ">
            <IconFolderOpen20Regular />
          </button>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.general-pane {
  display: contents;
}

.db-path-row {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-bottom: 10px;
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
  border-inline-start: 1px solid var(--border-color);
  border-radius: 0;
  background: transparent;
  color: var(--text-secondary);
}
.folder-btn:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
</style>
