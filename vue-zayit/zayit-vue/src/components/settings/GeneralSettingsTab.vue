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
              class="btn-primary"
              style="margin-top: 8px;">
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
             step="0.05" />
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
               class="database-path-input input-secondary"
               readonly />
        <button @click="$emit('selectDatabase')"
                class="btn-icon flex-center">
          📁
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import ThemePreviewDropdown from '@/components/settings/ThemePreviewDropdown.vue'
import { useGeneralSettingsTab } from '@/components/settings/useGeneralSettingsTab'

defineEmits<{
  createTheme: []
  deleteTheme: [id: string]
  selectDatabase: []
}>()

const {
  censorDivineNames,
  appZoom,
  databasePath,
  globalDiacritics,
  newTabPage,
  defaultBookViewToolbarPosition,
  themePreset,
  setCensorDivineNames,
  webviewBridge
} = useGeneralSettingsTab()
</script>

<style scoped>
.tab-pane {
  direction: rtl;
}

.database-path-row {
  gap: 8px;
}

.database-path-input {
  flex: 1;
  direction: ltr;
  text-align: left;
}
</style>
