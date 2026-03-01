<template>
    <div class="reading-bg-dropdown"
         ref="dropdownRef">
        <div class="dropdown-trigger"
             @click="isOpen = !isOpen">
            <span>{{ currentBackgroundName }}</span>
            <span class="dropdown-arrow">{{ isOpen ? '▲' : '▼' }}</span>
        </div>
        <div v-if="isOpen"
             class="dropdown-menu"
             :style="dropdownStyles">
            <!-- Default option with both light and dark -->
            <div class="dropdown-item">
                <div class="theme-name">ברירת מחדל</div>
                <ThemePreviewPair :light-colors="getBackgroundColors('fluent-light')"
                                  :dark-colors="getBackgroundColors('fluent-dark')"
                                  :active="modelValue === 'default' || modelValue === 'fluent-light' || modelValue === 'fluent-dark'"
                                  :interactive="false"
                                  @click="selectBackground('default')" />
            </div>

            <!-- Theme families with light/dark pairs -->
            <div v-for="family in themeFamilies.filter(f => f.family !== 'fluent')"
                 :key="family.family"
                 class="dropdown-item">
                <div class="theme-name">{{ family.name }}</div>
                <ThemePreviewPair :light-colors="getBackgroundColors(family.lightPreset)"
                                  :dark-colors="getBackgroundColors(family.darkPreset)"
                                  :active="modelValue === family.lightPreset || modelValue === family.darkPreset"
                                  :interactive="false"
                                  @click="selectBackgroundFamily(family)" />
            </div>

            <!-- Custom themes section -->
            <div v-if="customThemeFamilies.length > 0"
                 class="custom-themes-section">
                <div class="section-divider"></div>
                <div class="section-header">ערכות נושא מותאמות אישית</div>
                <div v-for="family in customThemeFamilies"
                     :key="family.family"
                     class="dropdown-item">
                    <div class="theme-name">{{ family.name }}</div>
                    <ThemePreviewPair v-if="family.lightTheme && family.darkTheme"
                                      :light-colors="family.lightTheme.reading"
                                      :dark-colors="family.darkTheme.reading"
                                      :active="modelValue === family.lightId || modelValue === family.darkId"
                                      :interactive="false"
                                      @click="selectBackgroundCustomFamily(family)" />
                    <ThemePreviewCard v-else-if="family.lightTheme"
                                      :colors="family.lightTheme.reading"
                                      label="בהיר"
                                      :active="modelValue === family.lightId"
                                      single
                                      @click="selectBackground(family.lightId!)" />
                    <ThemePreviewCard v-else-if="family.darkTheme"
                                      :colors="family.darkTheme.reading"
                                      label="כהה"
                                      :active="modelValue === family.darkId"
                                      single
                                      @click="selectBackground(family.darkId!)" />
                </div>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { onClickOutside } from '@vueuse/core'
import { useDropdownPosition } from '../composables/useDropdownPosition'
import { type ReadingBackgroundPreset } from '../config/readingBackgrounds'
import { getTheme, THEME_PRESETS, getThemeFamilies, getCustomThemes, type ThemeColors, type ThemePreset, type Theme } from '../config/themes'
import ThemePreviewCard from './ThemePreviewCard.vue'
import ThemePreviewPair from './ThemePreviewPair.vue'

const props = defineProps<{
    modelValue: ReadingBackgroundPreset
}>()

const emit = defineEmits<{
    'update:modelValue': [value: ReadingBackgroundPreset]
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

const currentBackgroundName = computed(() => {
    if (props.modelValue === 'default' || props.modelValue === 'fluent-light' || props.modelValue === 'fluent-dark') {
        return 'ברירת מחדל'
    }
    const theme = getTheme(props.modelValue as ThemePreset)
    if (!theme) return 'ברירת מחדל'
    return theme.name
})

function getBackgroundColors(preset: ReadingBackgroundPreset): ThemeColors {
    if (preset === 'default') {
        return THEME_PRESETS['fluent-light']!.reading
    }
    const theme = getTheme(preset as ThemePreset)
    return theme?.reading ?? THEME_PRESETS['fluent-light']!.reading
}

function selectBackground(preset: ReadingBackgroundPreset) {
    emit('update:modelValue', preset)
    isOpen.value = false
}

function selectBackgroundFamily(family: { lightPreset: ThemePreset; darkPreset: ThemePreset }) {
    // Select light variant by default
    emit('update:modelValue', family.lightPreset)
    isOpen.value = false
}

function selectBackgroundCustomFamily(family: CustomThemeFamily) {
    // Select light variant if available, otherwise dark
    const preset = family.lightId ?? family.darkId
    if (preset) {
        emit('update:modelValue', preset as ReadingBackgroundPreset)
        isOpen.value = false
    }
}
</script>

<style scoped>
.reading-bg-dropdown {
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
</style>
