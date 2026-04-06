<script setup lang="ts">
import { ref } from 'vue'
import SettingsGeneralPane from './SettingsGeneralPane.vue'
import SettingsReadingPane from './SettingsReadingPane.vue'
import SettingsAdvancedPane from './SettingsAdvancedPane.vue'

const activeTab = ref<'general' | 'reading' | 'advanced'>('general')
const tabIndexMap = { general: 0, reading: 1, advanced: 2 } as const
const tabCount = 3
</script>

<template>
  <div class="settings-page">
    <div
      class="tab-bar"
      :style="{ '--tab-index': tabIndexMap[activeTab], '--tab-count': tabCount }"
    >
      <button
        :class="['tab-btn', { active: activeTab === 'general' }]"
        @click="activeTab = 'general'"
      >
        כללי
      </button>
      <button
        :class="['tab-btn', { active: activeTab === 'reading' }]"
        @click="activeTab = 'reading'"
      >
        קריאה
      </button>
      <button
        :class="['tab-btn', { active: activeTab === 'advanced' }]"
        @click="activeTab = 'advanced'"
      >
        מתקדם
      </button>
    </div>

    <div v-if="activeTab === 'general'" class="pane">
      <SettingsGeneralPane />
    </div>

    <div v-if="activeTab === 'reading'" class="pane">
      <SettingsReadingPane />
    </div>

    <div v-if="activeTab === 'advanced'" class="pane">
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

.tab-bar {
  display: flex;
  flex-shrink: 0;
  border-bottom: 1px solid var(--border-color);
  background: var(--bg-toolbar);
  position: relative;
}

.tab-bar::after {
  content: '';
  position: absolute;
  bottom: 0;
  height: 2px;
  background: var(--accent-color);
  width: calc(100% / var(--tab-count));
  /* RTL: tab 0 is on the right, so index 0 = right edge */
  right: calc(var(--tab-index) * 100% / var(--tab-count));
  transition: right 0.2s cubic-bezier(0.4, 0, 0.2, 1);
}

.tab-btn {
  flex: 1;
  padding: 0 12px;
  height: 32px;
  border: none;
  border-bottom: 2px solid transparent;
  border-radius: 0;
  background: transparent;
  color: var(--text-secondary);
  font-size: 12px;
  cursor: pointer;
}
.tab-btn:hover {
  background: var(--hover-bg);
  color: var(--text-primary);
}
.tab-btn:active {
  transform: none;
}
.tab-btn.active {
  color: var(--text-primary);
  border-bottom-color: transparent;
}

.pane {
  flex: 1;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 12px 16px;
}
</style>
