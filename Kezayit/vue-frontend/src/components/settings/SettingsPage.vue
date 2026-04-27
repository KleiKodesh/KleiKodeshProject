<script setup lang="ts">
import { ref, computed } from 'vue'
import TabStrip from '@/components/common/TabStrip.vue'
import SettingsGeneralPane from './SettingsGeneralPane.vue'
import SettingsReadingPane from './SettingsReadingPane.vue'
import SettingsAdvancedPane from './SettingsAdvancedPane.vue'

const TABS = [
  { key: 'general', label: 'כללי' },
  { key: 'reading', label: 'קריאה' },
  { key: 'advanced', label: 'מתקדם' },
]

const PANE_MAP = {
  general: SettingsGeneralPane,
  reading: SettingsReadingPane,
  advanced: SettingsAdvancedPane,
}

const activeTab = ref('general')
const activePane = computed(() => PANE_MAP[activeTab.value as keyof typeof PANE_MAP])
</script>

<template>
  <div class="settings-page">
    <TabStrip v-model="activeTab" :tabs="TABS" />
    <div class="pane">
      <component :is="activePane" />
    </div>
  </div>
</template>

<style scoped>
.settings-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  direction: rtl;
  background: var(--bg-primary);
}

.pane {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 12px 16px;
}
</style>
