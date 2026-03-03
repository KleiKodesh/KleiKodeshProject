<template>
  <div class="tab-pane">
    <!-- Theme Selection -->
    <div class="setting-group">
      <label class="setting-label bold">ערכת נושא</label>
      <ThemePreviewDropdown v-model="themePreset"
                            :show-custom-themes="true"
                            :show-delete="true"
                            @delete="$emit('deleteTheme', $event)" />

      <button @click="$emit('createTheme')"
              class="create-theme-btn">
        ערכת נושא מותאמת אישית
      </button>
    </div>

    <!-- App Zoom -->
    <div class="setting-group">
      <label class="setting-label flex-between">
        <span class="bold">זום האפליקציה</span>
        <span class="text-secondary setting-value">{{ Math.round(appZoom * 100) }}%</span>
      </label>
      <input type="range"
             v-model.number="appZoom"
             min="0.5"
             max="1.5"
             step="0.05"
             class="setting-slider" />
    </div>

    <!-- New Tab Page -->
    <div class="setting-group">
      <label class="setting-label flex-between bold">דף ברירת מחדל לטאב חדש</label>
      <div class="button-group flex-row wrap">
        <button :class="['toggle-btn compact c-pointer', { active: newTabPage === 'homepage' }]"
                @click="newTabPage = 'homepage'">
          דף הבית
        </button>
        <button :class="['toggle-btn compact c-pointer', { active: newTabPage === 'openfile' }]"
                @click="newTabPage = 'openfile'">
          פתיחת ספר
        </button>
        <button :class="['toggle-btn compact c-pointer', { active: newTabPage === 'hebrewbooks' }]"
                @click="newTabPage = 'hebrewbooks'">
          היברו בוקס
        </button>
        <button :class="['toggle-btn compact c-pointer', { active: newTabPage === 'kezayit-search' }]"
                @click="newTabPage = 'kezayit-search'">
          חיפוש
        </button>
      </div>
    </div>

    <!-- Toolbar Position -->
    <div class="setting-group">
      <label class="setting-label flex-between bold">מיקום ברירת מחדל של סרגל הכלים</label>
      <div class="button-group flex-row wrap">
        <button :class="['toggle-btn compact c-pointer', { active: defaultBookViewToolbarPosition === 'top' }]"
                @click="defaultBookViewToolbarPosition = 'top'">
          למעלה
        </button>
        <button :class="['toggle-btn compact c-pointer', { active: defaultBookViewToolbarPosition === 'bottom' }]"
                @click="defaultBookViewToolbarPosition = 'bottom'">
          למטה
        </button>
        <button :class="['toggle-btn compact c-pointer', { active: defaultBookViewToolbarPosition === 'left' }]"
                @click="defaultBookViewToolbarPosition = 'left'">
          שמאל
        </button>
        <button :class="['toggle-btn compact c-pointer', { active: defaultBookViewToolbarPosition === 'right' }]"
                @click="defaultBookViewToolbarPosition = 'right'">
          ימין
        </button>
        <button :class="['toggle-btn compact c-pointer', { active: defaultBookViewToolbarPosition === 'float-vertical' }]"
                @click="defaultBookViewToolbarPosition = 'float-vertical'">
          צף מאונך
        </button>
        <button :class="['toggle-btn compact c-pointer', { active: defaultBookViewToolbarPosition === 'float-horizontal' }]"
                @click="defaultBookViewToolbarPosition = 'float-horizontal'">
          צף מאוזן
        </button>
      </div>
    </div>

    <!-- Diacritics Mode -->
    <div class="setting-group">
      <label class="setting-label flex-between bold">מצב טעמים וניקוד</label>
      <div class="button-group flex-row">
        <button :class="['toggle-btn c-pointer', { active: !globalDiacritics }]"
                @click="globalDiacritics = false">
          לכל טאב בנפרד
        </button>
        <button :class="['toggle-btn c-pointer', { active: globalDiacritics }]"
                @click="globalDiacritics = true">
          גלובלי
        </button>
      </div>
    </div>

    <!-- Divine Names Censoring -->
    <div class="setting-group">
      <label class="setting-label flex-between bold">כיסוי שם ה'</label>
      <div class="button-group flex-row">
        <button :class="['toggle-btn c-pointer', { active: !censorDivineNames }]"
                @click="setCensorDivineNames(false)">
          כתיב מלא
        </button>
        <button :class="['toggle-btn c-pointer', { active: censorDivineNames }]"
                @click="setCensorDivineNames(true)">
          כיסוי (ה→ק)
        </button>
      </div>
    </div>

    <!-- Database Path -->
    <div v-if="webviewBridge.isAvailable()"
         class="setting-group">
      <label class="setting-label flex-between bold">מיקום מסד הנתונים</label>
      <div class="database-path-row flex-row">
        <input type="text"
               v-model="databasePath"
               placeholder="בחר מיקום מסד הנתונים (seforim.db)"
               class="database-path-input"
               readonly />
        <button @click="$emit('selectDatabase')"
                class="c-pointer database-browse-btn flex-center">
          📁
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/data/stores/settingsStore'
import { webviewBridge } from '@/data/services/webviewBridge'
import ThemePreviewDropdown from '@/components/settings/ThemePreviewDropdown.vue'

defineEmits<{
  createTheme: []
  deleteTheme: [id: string]
  selectDatabase: []
}>()

const settingsStore = useSettingsStore()
const {
  censorDivineNames,
  appZoom,
  databasePath,
  globalDiacritics,
  newTabPage,
  defaultBookViewToolbarPosition,
  themePreset
} = storeToRefs(settingsStore)

const setCensorDivineNames = (censor: boolean) => {
  censorDivineNames.value = censor
  window.location.reload()
}

onMounted(() => {
  if (webviewBridge.isAvailable()) {
    webviewBridge
      .getCurrentDatabasePath()
      .then((p) => {
        if (p && !databasePath.value) databasePath.value = p
      })
      .catch(() => { })
  }
})
</script>

<style scoped>
.tab-pane {
  direction: rtl;
}

.setting-group {
  padding: 14px 16px;
  border-bottom: 1px solid var(--border-color);
}

.setting-group:last-child {
  border-bottom: none;
}

.setting-label {
  font-size: 14px;
  margin-bottom: 10px;
}

.setting-description {
  font-size: 13px;
  color: var(--text-secondary);
  margin-bottom: 10px;
  direction: rtl;
}

.setting-value {
  font-size: 13px;
  font-weight: normal;
}

.button-group {
  gap: 8px;
}

.button-group.wrap {
  flex-wrap: wrap;
}

.toggle-btn {
  flex: 1;
  padding: 10px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  font-size: 13px;
  transition: all 0.15s;
}

.toggle-btn.compact {
  padding: 8px 10px;
  font-size: 12px;
}

.toggle-btn:hover {
  background: var(--hover-bg);
  border-color: var(--accent-color);
}

.toggle-btn.active {
  background: var(--accent-color);
  color: #fff;
  border-color: var(--accent-color);
}

.setting-slider {
  width: 100%;
  height: 6px;
  background: var(--bg-secondary);
  border-radius: 3px;
  outline: none;
  -webkit-appearance: none;
  appearance: none;
}

.setting-slider::-webkit-slider-thumb {
  -webkit-appearance: none;
  width: 22px;
  height: 22px;
  background: var(--accent-color);
  border-radius: 50%;
  cursor: pointer;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.2);
}

.setting-slider::-moz-range-thumb {
  width: 22px;
  height: 22px;
  background: var(--accent-color);
  border-radius: 50%;
  cursor: pointer;
  border: none;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.2);
}

.database-path-row {
  gap: 8px;
}

.database-path-input {
  flex: 1;
  padding: 10px 12px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  font-size: 13px;
  direction: ltr;
  text-align: left;
}

.database-path-input:hover {
  border-color: var(--accent-color);
}

.database-browse-btn {
  width: 42px;
  height: 42px;
  flex-shrink: 0;
  background: var(--accent-color);
  border: none;
  border-radius: 8px;
  color: #fff;
  font-size: 16px;
  transition: all 0.15s;
}

.database-browse-btn:hover {
  transform: scale(1.05);
}

.create-theme-btn {
  width: 100%;
  padding: 10px 12px;
  background: var(--accent-color);
  color: white;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  font-size: 14px;
  font-weight: 500;
  transition: opacity 0.2s ease;
  margin-top: 8px;
}

.create-theme-btn:hover {
  opacity: 0.9;
}
</style>
