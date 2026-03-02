<template>
    <div class="theme-preview-dropdown"
         ref="dropdownRef">
        <div class="dropdown-trigger"
             @click.stop="isOpen = !isOpen">
            <span>{{ currentThemeName }}</span>
            <span class="dropdown-arrow">{{ isOpen ? '▲' : '▼' }}</span>
        </div>
        <div v-if="isOpen"
             class="dropdown-menu"
             :style="dropdownStyles">
            <!-- Built-in themes -->
            <div v-for="themeFamily in themeFamilies"
                 :key="themeFamily.family"
                 class="dropdown-item">
                <div class="theme-name">{{ themeFamily.name }}</div>
                <div class="theme-previews">
                    <ThemePreviewCard :colors="getThemePreset(themeFamily.lightPreset).reading"
                                      label="בהיר"
                                      :active="modelValue === themeFamily.lightPreset"
                                      @click="selectTheme(themeFamily.lightPreset)" />

                    <ThemePreviewCard :colors="getThemePreset(themeFamily.darkPreset).reading"
                                      label="כהה"
                                      :active="modelValue === themeFamily.darkPreset"
                                      @click="selectTheme(themeFamily.darkPreset)" />
                </div>
            </div>

            <!-- Custom themes section -->
            <div v-if="customThemeFamilies.length > 0 && showCustomThemes"
                 class="custom-themes-section">
                <div class="section-divider"></div>
                <div class="section-header">ערכות נושא מותאמות אישית</div>
                <div v-for="family in customThemeFamilies"
                     :key="family.family"
                     class="dropdown-item custom-theme-item">
                    <div class="theme-name">
                        {{ family.name }}
                        <button v-if="showDelete"
                                @click.stop="deleteFamily(family)"
                                class="delete-theme-btn"
                                title="מחק ערכת נושא">
                            🗑️
                        </button>
                    </div>
                    <div class="theme-previews">
                        <ThemePreviewCard v-if="family.lightTheme"
                                          :colors="family.lightTheme.reading"
                                          label="בהיר"
                                          :active="modelValue === family.lightId"
                                          @click="selectTheme(family.lightId!)" />

                        <ThemePreviewCard v-if="family.darkTheme"
                                          :colors="family.darkTheme.reading"
                                          label="כהה"
                                          :active="modelValue === family.darkId"
                                          @click="selectTheme(family.darkId!)" />
                    </div>
                </div>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { onClickOutside } from '@vueuse/core'
import { useDropdownPosition } from '../composables/useDropdownPosition'
import {
    THEME_PRESETS,
    getThemeFamilies,
    getCustomThemes,
    getTheme,
    type ThemePreset,
    type Theme
} from '../config/themes'
import ThemePreviewCard from './ThemePreviewCard.vue'

const props = withDefaults(defineProps<{
    modelValue: ThemePreset | ''
    placeholder?: string
    showCustomThemes?: boolean
    showDelete?: boolean
}>(), {
    placeholder: 'בחר ערכת נושא',
    showCustomThemes: true,
    showDelete: false
})

const emit = defineEmits<{
    'update:modelValue': [value: ThemePreset | '']
    'delete': [id: string]
}>()

const isOpen = ref(false)
const dropdownRef = ref<HTMLElement>()

const { dropdownStyles, updatePosition } = useDropdownPosition(dropdownRef, isOpen)

onClickOutside(dropdownRef, () => {
    isOpen.value = false
})

// Watch isOpen to update position when dropdown opens
watch(isOpen, () => {
    updatePosition()
})

const themeFamilies = computed(() => getThemeFamilies())

interface CustomThemeFamily {
    family: string
    name: string
    lightId?: string
    darkId?: string
    lightTheme?: Theme
    darkTheme?: Theme
}

const customThemeFamilies = computed((): CustomThemeFamily[] => {
    const customs = getCustomThemes()
    const familyMap = new Map<string, CustomThemeFamily>()

    Object.entries(customs).forEach(([id, theme]) => {
        if (!familyMap.has(theme.family)) {
            familyMap.set(theme.family, {
                family: theme.family,
                name: theme.name
            })
        }
        const family = familyMap.get(theme.family)!
        if (theme.isDark) {
            family.darkId = id
            family.darkTheme = theme
        } else {
            family.lightId = id
            family.lightTheme = theme
        }
    })

    return Array.from(familyMap.values())
})

const currentThemeName = computed(() => {
    if (!props.modelValue) return props.placeholder
    const theme = getTheme(props.modelValue)
    if (!theme) return props.placeholder
    return `${theme.name} (${theme.isDark ? 'כהה' : 'בהיר'})`
})

function getThemePreset(preset: ThemePreset): Theme {
    const theme = THEME_PRESETS[preset]
    return theme ?? THEME_PRESETS['fluent-light']!
}

function selectTheme(preset: ThemePreset) {
    emit('update:modelValue', preset)
    isOpen.value = false
}

function deleteFamily(family: CustomThemeFamily) {
    if (family.lightId) emit('delete', family.lightId)
    if (family.darkId) emit('delete', family.darkId)
}
</script>

<style scoped>
.theme-preview-dropdown {
    position: relative;
}

.dropdown-trigger {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 8px 12px;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 6px;
    cursor: pointer;
}

.dropdown-trigger:hover {
    border-color: var(--accent-color);
}

.dropdown-arrow {
    font-size: 10px;
    color: var(--text-secondary);
}

.dropdown-menu {
    position: absolute;
    left: 0;
    right: 0;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 6px;
    overflow-y: auto;
    z-index: 1000;
}

.dropdown-item {
    padding: 8px;
}

.theme-name {
    font-size: 13px;
    font-weight: bold;
    margin-bottom: 8px;
    color: var(--text-primary);
    display: flex;
    align-items: center;
    justify-content: space-between;
}

.theme-previews {
    display: flex;
    gap: 8px;
}

.custom-themes-section {
    margin-top: 8px;
}

.section-divider {
    height: 1px;
    background: var(--border-color);
    margin: 8px 0;
}

.section-header {
    font-size: 12px;
    font-weight: bold;
    color: var(--text-secondary);
    padding: 4px 8px;
    text-transform: uppercase;
    letter-spacing: 0.5px;
}

.custom-theme-item {
    position: relative;
}

.delete-theme-btn {
    background: none;
    border: none;
    cursor: pointer;
    font-size: 14px;
    padding: 2px 6px;
    border-radius: 3px;
    opacity: 0.6;
    transition: opacity 0.2s ease, background 0.2s ease;
}

.delete-theme-btn:hover {
    opacity: 1;
    background: var(--hover-bg);
}
</style>
