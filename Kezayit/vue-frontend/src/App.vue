<script setup lang="ts">
import AppTitleBar from '@/layout/AppTitleBar.vue'
import AppPageView from '@/layout/AppPageView.vue'
import SetupWizard from '@/features/settings/SetupWizard.vue'
import { resetting } from '@/utils/appResetState'
import { useSettingsStore } from '@/stores/settingsStore'
import { storeToRefs } from 'pinia'

const settingsStore = useSettingsStore()
const { setupDone } = storeToRefs(settingsStore)
</script>

<template>
  <div class="app-layout">
    <AppTitleBar />
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
