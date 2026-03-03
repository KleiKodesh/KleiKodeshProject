<template>
  <div class="flex-column width-fill height-fill settings-page">
    <!-- Tab Bar -->
    <div class="flex-row tab-bar">
      <template v-if="!showCustomThemeCreator">
        <button
          :class="['tab-btn flex-center', { active: activeTab === 'general' }]"
          @click="activeTab = 'general'"
        >
          כללי
        </button>
        <button
          :class="['tab-btn flex-center', { active: activeTab === 'reading' }]"
          @click="activeTab = 'reading'"
        >
          קריאה
        </button>
        <button class="tab-btn tab-btn--reset flex-center" @click="resetSettings">
          ↺ איפוס
        </button>
      </template>
      <template v-else>
        <div class="tab-btn flex-center active theme-creator-title">יצירת ערכת נושא</div>
      </template>
    </div>

    <!-- Tab Content -->
    <div class="flex-110 overflow-y settings-content">
      <ReadingSettingsTab v-if="activeTab === 'reading'" />
      <GeneralSettingsTab
        v-if="activeTab === 'general'"
        @create-theme="openCustomThemeCreator"
        @delete-theme="deleteCustomThemeHandler"
        @select-database="selectDatabaseFile"
      />
    </div>

    <!-- Custom Dialog -->
    <CustomDialog
      ref="dialogRef"
      :title="dialogOptions.title"
      :message="dialogOptions.message"
      :icon="dialogOptions.icon"
      :icon-type="dialogOptions.iconType"
      :confirm-text="dialogOptions.confirmText"
      :cancel-text="dialogOptions.cancelText"
      :confirm-variant="dialogOptions.confirmVariant"
      :show-confirm="dialogOptions.showConfirm"
      :show-cancel="dialogOptions.showCancel"
      :show-close-button="dialogOptions.showCloseButton"
      :show-actions="dialogOptions.showActions"
      :size="dialogOptions.size"
      :close-on-overlay="dialogOptions.closeOnOverlay"
      @confirm="handleConfirm"
      @cancel="handleCancel"
      @close="handleClose"
    />

    <!-- Custom Theme Creator Overlay -->
    <div v-if="showCustomThemeCreator" class="theme-creator-overlay">
      <ThemeCreator @close="closeCustomThemeCreator" @save="handleCustomThemeSave" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/data/stores/settingsStore'
import type { SettingsTab } from '@/data/stores/settingsStore'
import { webviewBridge } from '@/data/services/webviewBridge'
import { useDialog } from '@/components/shared/useDialog'
import CustomDialog from '@/components/shared/CustomDialog.vue'
import ThemeCreator from '@/components/settings/ThemeCreator.vue'
import ReadingSettingsTab from '@/components/settings/ReadingSettingsTab.vue'
import GeneralSettingsTab from '@/components/settings/GeneralSettingsTab.vue'
import {
  addCustomTheme,
  deleteCustomTheme,
  getTheme,
  type ThemePreset,
  type ThemeColors
} from '@/utils/themes'

const settingsStore = useSettingsStore()
const { databasePath, themePreset, lastSettingsTab } = storeToRefs(settingsStore)
const { dialogRef, dialogOptions, confirm, error, handleConfirm, handleCancel, handleClose } =
  useDialog()

const activeTab = ref<SettingsTab>(lastSettingsTab.value)
const showCustomThemeCreator = ref(false)

// Watch activeTab and update lastSettingsTab in store
watch(activeTab, (newTab) => {
  lastSettingsTab.value = newTab
})

// Reset settings
const resetSettings = async () => {
  const confirmed = await confirm(
    'האם אתה בטוח שברצונך לאפס את כל ההגדרות? פעולה זו תחזיר את האפליקציה למצב ברירת המחדל.',
    { title: 'איפוס הגדרות', confirmVariant: 'danger' }
  )
  if (!confirmed) return

  settingsStore.reset()

  if (webviewBridge.isAvailable()) {
    try {
      await webviewBridge.clearDatabasePath()
    } catch {}
  }

  window.location.reload()
}

// Database file selection
const selectDatabaseFile = async () => {
  try {
    const result = await webviewBridge.openDatabaseFilePicker()
    if (!result.filePath) return

    const isValid = await webviewBridge.validateDatabasePath(result.filePath)
    if (!isValid) {
      await error('הקובץ שנבחר אינו מסד נתונים תקין של SQLite.')
      return
    }

    databasePath.value = result.filePath
    const ok = await webviewBridge.setDatabasePath(result.filePath)

    if (ok) {
      window.location.reload()
    } else {
      await error('שגיאה בהגדרת מיקום מסד הנתונים. אנא נסה שוב.')
      databasePath.value = ''
    }
  } catch {
    await error('שגיאה בבחירת קובץ מסד הנתונים. אנא נסה שוב.')
  }
}

// Theme creator functions
function openCustomThemeCreator() {
  showCustomThemeCreator.value = true
}

function closeCustomThemeCreator() {
  showCustomThemeCreator.value = false
}

function handleCustomThemeSave(
  themes: Array<{
    id: string
    name: string
    isDark: boolean
    reading: ThemeColors
    ui: ThemeColors
  }>
) {
  themes.forEach((themeData) => {
    const theme = {
      name: themeData.name,
      isDark: themeData.isDark,
      family: themeData.id.replace(/-light$|-dark$/, ''),
      reading: themeData.reading,
      ui: themeData.ui
    }
    addCustomTheme(themeData.id, theme)
  })

  if (themes.length > 0 && themes[0]) {
    themePreset.value = themes[0].id as ThemePreset
  }

  closeCustomThemeCreator()
}

async function deleteCustomThemeHandler(id: string) {
  const confirmed = await confirm(
    `האם למחוק את ערכת הנושא "${getTheme(id as ThemePreset)?.name}"?`
  )
  if (confirmed) {
    deleteCustomTheme(id)
    if (themePreset.value === id) {
      themePreset.value = 'fluent-light'
    }
  }
}
</script>

<style scoped>
.settings-page {
  background: var(--reading-bg);
  position: relative;
}

.tab-bar {
  border-bottom: 1px solid var(--border-color);
  background: var(--bg-secondary);
  flex-shrink: 0;
  gap: 0;
}

.tab-btn {
  flex: 1;
  padding: 10px 6px;
  background: none;
  border: none;
  border-radius: 0;
  color: var(--text-secondary);
  font-size: 0.875rem;
  white-space: nowrap;
  transition: color 0.15s, background 0.15s;
}

.tab-btn:hover {
  color: var(--text-primary);
  background: var(--hover-bg);
}

.tab-btn.active {
  background: var(--hover-bg);
  color: var(--text-primary);
  font-weight: 700;
}

.tab-btn--reset:hover {
  color: #e53e3e;
  background: color-mix(in srgb, #e53e3e 8%, transparent);
}

.theme-creator-title {
  flex: 1;
  font-size: 1rem;
}

.settings-content {
  padding: 0;
}

.theme-creator-overlay {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  z-index: 1000;
}
</style>
