<template>
  <div class="flex-column width-fill height-fill settings-page">
    <!-- Tab Bar -->
    <div class="flex-row tab-bar">
      <template v-if="!showCustomThemeCreator">
        <button :class="['tab-btn flex-center', { active: activeTab === 'general' }]"
                @click="activeTab = 'general'">
          כללי
        </button>
        <button :class="['tab-btn flex-center', { active: activeTab === 'reading' }]"
                @click="activeTab = 'reading'">
          קריאה
        </button>
        <button class="tab-btn tab-btn--reset flex-center"
                @click="resetSettings">
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
      <GeneralSettingsTab v-if="activeTab === 'general'"
                          @create-theme="openCustomThemeCreator"
                          @delete-theme="deleteCustomThemeHandler"
                          @select-database="selectDatabaseFile" />
    </div>

    <!-- Custom Dialog -->
    <CustomDialog ref="dialogRef"
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
                  @close="handleClose" />

    <!-- Custom Theme Creator Overlay -->
    <div v-if="showCustomThemeCreator"
         class="theme-creator-overlay">
      <ThemeCreator @close="closeCustomThemeCreator"
                    @save="handleCustomThemeSave" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { useDialog } from '@/components/shared/useDialog'
import { useSettingsPage } from '@/components/settings/useSettingsPage'
import CustomDialog from '@/components/shared/CustomDialog.vue'
import ThemeCreator from '@/components/settings/ThemeCreator.vue'
import ReadingSettingsTab from '@/components/settings/ReadingSettingsTab.vue'
import GeneralSettingsTab from '@/components/settings/GeneralSettingsTab.vue'

const { dialogRef, dialogOptions, confirm, error, handleConfirm, handleCancel, handleClose } = useDialog()

const {
  activeTab,
  showCustomThemeCreator,
  resetSettings,
  selectDatabaseFile,
  openCustomThemeCreator,
  closeCustomThemeCreator,
  handleCustomThemeSave,
  deleteCustomThemeHandler
} = useSettingsPage(confirm, error)
</script>

<style scoped>
.settings-page {
  background: var(--ui-reading-bg);
  position: relative;
}

.tab-bar {
  border-bottom: 1px solid var(--border-color);
  background: var(--bg-secondary);
  flex-shrink: 0;
  gap: 0;
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
