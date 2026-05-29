<script setup lang="ts">
import AppTitleBar from '@/layout/AppTitleBar.vue'
import AppPageView from '@/layout/AppPageView.vue'
import SetupWizard from '@/features/settings/SetupWizard.vue'
import { resetting } from '@/features/settings/appResetState'
import { useSettingsStore } from '@/stores/settingsStore'
import { useUiChromeVisibility } from '@/composables/useUiChromeVisibility'
import { storeToRefs } from 'pinia'

const settingsStore = useSettingsStore()
const { setupDone } = storeToRefs(settingsStore)
const { titleBarVisible } = useUiChromeVisibility()
</script>

<template>
  <div class="app-layout">
    <AppTitleBar v-if="titleBarVisible" />
    <main class="app-content">
      <AppPageView />
    </main>
    <SetupWizard v-if="!setupDone" />
    <div v-if="resetting" class="reset-overlay" />
  </div>
</template>

<style scoped>
.app-layout {
  display: flex;
  flex-direction: column;
  height: 100%;
}
.app-content {
  flex: 1;
  overflow: hidden;
}
.reset-overlay {
  position: fixed;
  inset: 0;
  z-index: 9999;
  background: rgba(0, 0, 0, 0.4);
  pointer-events: all;
}
</style>
