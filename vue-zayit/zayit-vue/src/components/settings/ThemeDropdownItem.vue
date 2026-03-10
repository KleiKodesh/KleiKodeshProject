<template>
    <div @click="handleToggle"
         class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
        <Icon :icon="themeIcon"
              class="theme-icon" />
        <span class="dropdown-label">{{ themeText }}</span>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { Icon } from '@iconify/vue'
import { useSettings } from '@/components/settings/useSettings'
import { toggleThemeMode, THEME_PRESETS, syncPdfViewerTheme } from '@/utils/themes'

const { themePreset } = useSettings()

const isDark = computed(() => {
    const theme = THEME_PRESETS[themePreset.value]
    return theme?.isDark ?? false
})

const themeIcon = computed(() => {
    return isDark.value ? 'fluent:weather-sunny-24-regular' : 'fluent:dark-theme-24-regular'
})

const themeText = computed(() => {
    return isDark.value ? 'מצב בהיר' : 'מצב כהה'
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

<style scoped>
.dropdown-item {
    gap: 12px;
    width: 100%;
    padding: 10px 16px;
    background: transparent;
    border: none;
    text-align: right;
    direction: rtl;
    color: var(--text-primary);
    border-radius: 0;
    transition: background-color 0.15s ease;
    opacity: 0.8;
    flex-shrink: 0;
}

.dropdown-item:hover {
    background: var(--hover-bg);
    opacity: 0.9;
}

.dropdown-item:active {
    background: var(--active-bg);
}

.theme-icon {
    flex-shrink: 0;
    width: 20px;
    height: 20px;
    color: var(--text-primary);
}

.dropdown-label {
    font-size: 14px;
    color: var(--text-primary);
    white-space: nowrap;
}
</style>
