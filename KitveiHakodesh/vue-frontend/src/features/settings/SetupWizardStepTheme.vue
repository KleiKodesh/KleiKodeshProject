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
</template>

