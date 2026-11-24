<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import ToggleGroup from '@/features/settings/ToggleGroup.vue'

const { keepScreenOnMode, keepScreenOnIdleMinutes } = storeToRefs(useSettingsStore())
</script>

<template>
  <div class="keep-screen-widget">
    <section class="keep-screen-section">
      <div class="keep-screen-label" :title="$t('settings.keepScreenOn.hint')">{{ $t('settings.keepScreenOn.label') }}</div>
      <ToggleGroup
        v-model="keepScreenOnMode"
        :options="[
          { label: $t('settings.keepScreenOn.off'), value: 'off' },
          { label: $t('settings.keepScreenOn.reading'), value: 'reading' },
          { label: $t('settings.keepScreenOn.fullscreen'), value: 'fullscreen' },
          { label: $t('settings.keepScreenOn.always'), value: 'always' },
        ]"
      />
    </section>
    <section v-if="keepScreenOnMode !== 'off'" class="keep-screen-section">
      <div class="keep-screen-label" :title="$t('settings.keepScreenOn.idleHint')">{{ $t('settings.keepScreenOn.idleLabel') }}</div>
      <ToggleGroup
        v-model="keepScreenOnIdleMinutes"
        :options="[
          { label: '5', value: 5 },
          { label: '10', value: 10 },
          { label: '30', value: 30 },
          { label: '60', value: 60 },
          { label: $t('settings.keepScreenOn.never'), value: 0 },
        ]"
      />
    </section>
  </div>
</template>

<style scoped>
.keep-screen-widget { display: flex; flex-direction: column; gap: 16px; padding: 12px; overflow-y: auto; }
.keep-screen-section { display: flex; flex-direction: column; gap: 8px; }
.keep-screen-label { font-size: 13px; color: var(--text-primary); }
</style>
