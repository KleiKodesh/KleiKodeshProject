<script setup lang="ts">
import { uiLanguage, setUiLanguage, type UiLanguage } from '@/i18n'
import ToggleGroup from './ToggleGroup.vue'

// The UI language control, shared by the settings page (full ToggleGroup row) and the
// setup wizard footer (`compact`: a globe link to the other language). Choosing reloads the page.
defineProps<{ compact?: boolean }>()

const OPTIONS: { label: string; value: UiLanguage }[] = [
  { label: 'עברית', value: 'he' },
  { label: 'English', value: 'en' },
]

// Fixed per page load (switching reloads), so a plain constant.
const otherOption = OPTIONS.find(o => o.value !== uiLanguage)!
</script>

<template>
  <!-- Compact (wizard footer): one quiet link naming the OTHER language, globe-prefixed,
       the convention of OOBE / sign-in pages. Each label is in its own language. -->
  <button
    v-if="compact"
    class="lang-switch"
    :lang="otherOption.value"
    :aria-label="$t('settings.app.uiLanguage')"
    @click="setUiLanguage(otherOption.value)"
  >
    <svg class="lang-globe" viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
      <circle cx="12" cy="12" r="9" />
      <path d="M3 12h18M12 3c2.5 2.6 3.8 5.6 3.8 9s-1.3 6.4-3.8 9c-2.5-2.6-3.8-5.6-3.8-9S9.5 5.6 12 3z" />
    </svg>
    {{ otherOption.label }}
  </button>
  <ToggleGroup
    v-else
    :model-value="uiLanguage"
    :options="OPTIONS.map(o => ({ label: o.label, value: o.value }))"
    @update:model-value="(lang: UiLanguage) => setUiLanguage(lang)"
  />
</template>

<style scoped>
.lang-switch {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 28px;
  padding: 0 8px;
  font-size: 12px;
  background: transparent;
  color: var(--text-secondary);
  border: none;
  border-radius: 4px;
}
.lang-switch:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.lang-globe {
  flex-shrink: 0;
}
</style>
