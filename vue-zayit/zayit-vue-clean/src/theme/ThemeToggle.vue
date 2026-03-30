<script setup lang="ts">
import { computed } from 'vue'
import {
  IconBrightnessHigh20Regular,
  IconWeatherMoon20Regular,
} from '@iconify-prerendered/vue-fluent'
import { useThemeStore } from './themeStore'

const store = useThemeStore()
const isDark = computed(() => store.themePreset.includes('-dark'))
</script>

<template>
  <button
    class="theme-toggle"
    :title="isDark ? 'עבור למצב בהיר' : 'עבור למצב כהה'"
    @click="store.toggleDarkMode()"
  >
    <IconWeatherMoon20Regular v-if="isDark" />
    <IconBrightnessHigh20Regular v-else />
  </button>
</template>

<style scoped>
.theme-toggle {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  padding: 6px;
  border-radius: 0;
}
.theme-toggle svg {
  width: 16px;
  height: 16px;
}
</style>
