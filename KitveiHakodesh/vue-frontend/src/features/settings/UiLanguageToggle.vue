<script setup lang="ts">
import { uiLanguage, setUiLanguage, type UiLanguage } from '@/i18n'
import ToggleGroup from './ToggleGroup.vue'

// The UI language control, shared by the settings page (full ToggleGroup row) and the
// setup wizard footer (`compact`: a small two-segment pill). Choosing reloads the page.
defineProps<{ compact?: boolean }>()

const OPTIONS: { label: string; short: string; value: UiLanguage }[] = [
  { label: 'עברית', short: 'עב', value: 'he' },
  { label: 'English', short: 'EN', value: 'en' },
]
</script>

<template>
  <div v-if="compact" class="lang-pill" role="group" :aria-label="$t('settings.app.uiLanguage')">
    <button
      v-for="opt in OPTIONS"
      :key="opt.value"
      :class="['lang-seg', { active: uiLanguage === opt.value }]"
      :title="opt.label"
      @click="setUiLanguage(opt.value)"
    >
      {{ opt.short }}
    </button>
  </div>
  <ToggleGroup
    v-else
    :model-value="uiLanguage"
    :options="OPTIONS.map(o => ({ label: o.label, value: o.value }))"
    @update:model-value="(lang: UiLanguage) => setUiLanguage(lang)"
  />
</template>

<style scoped>
.lang-pill {
  display: inline-flex;
  height: 28px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  overflow: hidden;
}
.lang-seg {
  padding: 0 10px;
  font-size: 12px;
  background: transparent;
  color: var(--text-secondary);
  border: none;
}
.lang-seg + .lang-seg {
  border-inline-start: 1px solid var(--border-color);
}
.lang-seg:hover:not(.active) {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.lang-seg.active {
  background: var(--accent-color);
  color: #fff;
  font-weight: 600;
}
</style>
