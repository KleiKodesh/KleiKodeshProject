<script setup lang="ts">
import { ref } from 'vue'
import TabStrip from '@/components/common/TabStrip.vue'
import SettingsGeneralPane from './SettingsGeneralPane.vue'
import SettingsReadingPane from './SettingsReadingPane.vue'
import SettingsAdvancedPane from './SettingsAdvancedPane.vue'

const TABS = [
  { key: 'general', label: 'כללי' },
  { key: 'reading', label: 'קריאה' },
  { key: 'advanced', label: 'מתקדם' },
]

const activeTab = ref('general')
</script>

<template>
  <div class="settings-page">
    <TabStrip v-model="activeTab" :tabs="TABS" />

    <div v-show="activeTab === 'general'" class="pane">
      <SettingsGeneralPane />
    </div>
    <div v-show="activeTab === 'reading'" class="pane">
      <SettingsReadingPane />
    </div>
    <div v-show="activeTab === 'advanced'" class="pane">
      <SettingsAdvancedPane />
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
