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
import { useSettings } from '@/components/settings/useSettings'
import { toggleThemeMode, THEME_PRESETS } from '@/utils/themes'
import { syncPdfViewerTheme } from '@/utils/themes'

const { themePreset } = useSettings()

const isDark = computed(() => {
    const theme = THEME_PRESETS[themePreset.value]
    return theme?.isDark ?? false
})

function handleToggle() {
    const newPreset = toggleThemeMode(themePreset.value)
    themePreset.value = newPreset

    // Sync with PDF viewers
    setTimeout(() => {
        syncPdfViewerTheme()
    }, 100)
}
</script>
