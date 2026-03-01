<template>
    <button @click="handleToggle"
            class="flex-center c-pointer touch-interactive"
            :title="isDark ? 'מצב בהיר' : 'מצב כהה'">
        <Icon :icon="isDark ? 'fluent:weather-sunny-24-regular' : 'fluent:dark-theme-24-regular'" />
    </button>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { Icon } from '@iconify/vue'
import { useSettingsStore } from '../stores/settingsStore'
import { toggleThemeMode, THEME_PRESETS } from '../config/themes'
import { syncPdfViewerTheme } from '../utils/theme'

const settingsStore = useSettingsStore()

const isDark = computed(() => {
    const theme = THEME_PRESETS[settingsStore.themePreset]
    return theme?.isDark ?? false
})

function handleToggle() {
    const newPreset = toggleThemeMode(settingsStore.themePreset)
    settingsStore.themePreset = newPreset

    // Sync with PDF viewers
    setTimeout(() => {
        syncPdfViewerTheme()
    }, 100)
}
</script>
