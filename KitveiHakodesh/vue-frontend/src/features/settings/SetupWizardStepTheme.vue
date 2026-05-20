<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import SettingRow from './SettingRow.vue'
import SliderSetting from './SliderSetting.vue'
import ThemePicker from './ThemePicker.vue'

const settings = useSettingsStore()
const { appZoom } = storeToRefs(settings)
</script>

<template>
  <div class="step-content">
    <div class="step-header">
      <h2 class="step-title">איך תרצה שהאפליקציה תיראה?</h2>
      <p class="step-desc">בחר ערכת נושא וגודל תצוגה שנוחים לך.</p>
    </div>
    <div class="step-scroll">
      <div class="step-card">
        <SettingRow label="ערכת נושא">
          <ThemePicker />
        </SettingRow>
        <SliderSetting
          label="גודל תצוגה"
          v-model="appZoom"
          :min="0.5"
          :max="1.5"
          :step="0.05"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.step-content {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

.step-header {
  flex-shrink: 0;
  max-width: 560px;
  width: 100%;
  margin: 0 auto;
  padding: 28px 16px 12px;
  display: flex;
  flex-direction: column;
  gap: 6px;
  box-sizing: border-box;
}

.step-title {
  margin: 0;
  font-size: 20px;
  font-weight: 700;
  color: var(--text-primary);
  line-height: 1.2;
  animation: fade-up 0.25s ease both;
}

.step-desc {
  margin: 0;
  font-size: 13px;
  color: var(--text-secondary);
  line-height: 1.6;
  animation: fade-up 0.25s 0.05s ease both;
}

.step-scroll {
  flex: 1;
  overflow-y: auto;
  padding: 0 16px 24px;
}

.step-card {
  max-width: 560px;
  margin: 0 auto;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 16px 20px;
  animation: fade-up 0.25s 0.1s ease both;
}

@keyframes fade-up {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
</style>
