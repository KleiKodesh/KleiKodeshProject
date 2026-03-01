<template>
    <div class="custom-theme-creator">
        <div class="creator-content">
            <div class="form-group">
                <label>שם <span class="required-star">*</span></label>
                <input v-model="themeName"
                       type="text"
                       placeholder="שם ערכת הנושא" />
            </div>

            <div class="form-group">
                <label>רקע קריאה</label>
                <ThemePreviewDropdown v-model="baseTheme"
                                      placeholder="התחל מאפס"
                                      :show-custom-themes="false"
                                      @update:model-value="loadBaseTheme" />
            </div>

            <div class="color-picker-section">
                <div class="color-picker-row">
                    <label>רקע</label>
                    <input type="color"
                           v-model="backgroundColor" />
                </div>
                <div class="color-picker-row">
                    <label>טקסט</label>
                    <input type="color"
                           v-model="textColor" />
                </div>
                <div class="color-picker-row">
                    <label>הדגשה</label>
                    <input type="color"
                           v-model="accentColor" />
                </div>
            </div>

            <div class="preview-section">
                <div class="preview-label">תצוגה מקדימה</div>
                <ThemePreviewPair :light-colors="lightVariantColors"
                                  :dark-colors="darkVariantColors"
                                  :active-variant="currentVariant"
                                  :interactive="true"
                                  @click:light="loadLightVariant"
                                  @click:dark="loadDarkVariant" />
            </div>
        </div>

        <div class="actions">
            <button @click="saveTheme"
                    class="save-btn"
                    :disabled="!themeName.trim()">
                שמור
            </button>
            <button @click="$emit('close')"
                    class="cancel-btn">
                ביטול
            </button>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { THEME_PRESETS, type ThemePreset, type ThemeColors } from '../config/themes'
import ThemePreviewDropdown from './ThemePreviewDropdown.vue'
import ThemePreviewPair from './ThemePreviewPair.vue'

const emit = defineEmits<{
    close: []
    save: [themes: Array<{ id: string; name: string; isDark: boolean; reading: ThemeColors; ui: ThemeColors }>]
}>()

const themeName = ref('')
const baseTheme = ref<ThemePreset | ''>('')
const currentVariant = ref<'light' | 'dark'>('light')

// Simple user inputs - only 3 colors
const backgroundColor = ref('#ffffff')
const textColor = ref('#1f1f1f')
const accentColor = ref('#0078d4')

const loadBaseTheme = () => {
    if (!baseTheme.value) return
    const theme = THEME_PRESETS[baseTheme.value]
    if (theme) {
        backgroundColor.value = theme.ui.bgPrimary
        textColor.value = theme.ui.textPrimary
        accentColor.value = theme.ui.accentColor
        currentVariant.value = theme.isDark ? 'dark' : 'light'
    }
}

// Auto-generate secondary colors based on primary colors
function lighten(color: string, amount: number): string {
    const hex = color.replace('#', '')
    const r = Math.min(255, parseInt(hex.substring(0, 2), 16) + amount)
    const g = Math.min(255, parseInt(hex.substring(2, 4), 16) + amount)
    const b = Math.min(255, parseInt(hex.substring(4, 6), 16) + amount)
    return '#' + [r, g, b].map(x => Math.round(x).toString(16).padStart(2, '0')).join('')
}

function darken(color: string, amount: number): string {
    return lighten(color, -amount)
}

function adjustAlpha(isDark: boolean): { hover: string; active: string } {
    const baseAlpha = isDark ? 0.08 : 0.06
    const activeAlpha = isDark ? 0.12 : 0.09
    return {
        hover: `rgba(${isDark ? '255, 255, 255' : '0, 0, 0'}, ${baseAlpha})`,
        active: `rgba(${isDark ? '255, 255, 255' : '0, 0, 0'}, ${activeAlpha})`
    }
}

// Auto-detect if theme is dark
function isDarkTheme(bgColor: string): boolean {
    const hex = bgColor.replace('#', '')
    const r = parseInt(hex.substring(0, 2), 16)
    const g = parseInt(hex.substring(2, 4), 16)
    const b = parseInt(hex.substring(4, 6), 16)
    const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255
    return luminance < 0.5
}

// Computed colors - auto-generated from user's 3 basic colors
const computedReadingColors = computed((): ThemeColors => {
    const isDark = isDarkTheme(backgroundColor.value)
    const alphas = adjustAlpha(isDark)

    return {
        bgPrimary: backgroundColor.value,
        bgSecondary: isDark ? lighten(backgroundColor.value, 10) : darken(backgroundColor.value, 8),
        textPrimary: textColor.value,
        textSecondary: isDark ? darken(textColor.value, 40) : lighten(textColor.value, 60),
        borderColor: isDark ? lighten(backgroundColor.value, 20) : darken(backgroundColor.value, 15),
        accentColor: accentColor.value,
        hoverBg: alphas.hover,
        activeBg: alphas.active
    }
})

const computedUiColors = computed((): ThemeColors => {
    const isDark = isDarkTheme(backgroundColor.value)
    const alphas = adjustAlpha(isDark)

    return {
        bgPrimary: isDark ? lighten(backgroundColor.value, 5) : darken(backgroundColor.value, 4),
        bgSecondary: isDark ? lighten(backgroundColor.value, 15) : darken(backgroundColor.value, 12),
        textPrimary: textColor.value,
        textSecondary: isDark ? darken(textColor.value, 40) : lighten(textColor.value, 60),
        borderColor: isDark ? lighten(backgroundColor.value, 25) : darken(backgroundColor.value, 20),
        accentColor: accentColor.value,
        hoverBg: alphas.hover,
        activeBg: alphas.active
    }
})

// Invert colors for opposite variant
function invertColors(colors: ThemeColors): ThemeColors {
    const invertHex = (hex: string): string => {
        const h = hex.replace('#', '')
        const r = 255 - parseInt(h.substring(0, 2), 16)
        const g = 255 - parseInt(h.substring(2, 4), 16)
        const b = 255 - parseInt(h.substring(4, 6), 16)
        return '#' + [r, g, b].map(x => x.toString(16).padStart(2, '0')).join('')
    }

    return {
        bgPrimary: invertHex(colors.bgPrimary),
        bgSecondary: invertHex(colors.bgSecondary),
        textPrimary: invertHex(colors.textPrimary),
        textSecondary: invertHex(colors.textSecondary),
        borderColor: invertHex(colors.borderColor),
        accentColor: colors.accentColor,
        hoverBg: colors.hoverBg,
        activeBg: colors.activeBg
    }
}

// Light and dark variant colors for preview
const lightVariantColors = computed(() => {
    const isDark = isDarkTheme(backgroundColor.value)
    return isDark ? invertColors(computedReadingColors.value) : computedReadingColors.value
})

const darkVariantColors = computed(() => {
    const isDark = isDarkTheme(backgroundColor.value)
    return isDark ? computedReadingColors.value : invertColors(computedReadingColors.value)
})

// Load light variant colors into the form
function loadLightVariant() {
    currentVariant.value = 'light'
    const colors = lightVariantColors.value
    backgroundColor.value = colors.bgPrimary
    textColor.value = colors.textPrimary
    accentColor.value = colors.accentColor
}

// Load dark variant colors into the form
function loadDarkVariant() {
    currentVariant.value = 'dark'
    const colors = darkVariantColors.value
    backgroundColor.value = colors.bgPrimary
    textColor.value = colors.textPrimary
    accentColor.value = colors.accentColor
}

const saveTheme = () => {
    if (!themeName.value.trim()) return

    const timestamp = Date.now()
    const baseName = themeName.value.trim()
    const isDark = isDarkTheme(backgroundColor.value)

    const lightTheme = {
        id: `custom-${timestamp}-light`,
        name: baseName,
        isDark: false,
        reading: isDark ? invertColors(computedReadingColors.value) : computedReadingColors.value,
        ui: isDark ? invertColors(computedUiColors.value) : computedUiColors.value
    }

    const darkTheme = {
        id: `custom-${timestamp}-dark`,
        name: baseName,
        isDark: true,
        reading: isDark ? computedReadingColors.value : invertColors(computedReadingColors.value),
        ui: isDark ? computedUiColors.value : invertColors(computedUiColors.value)
    }

    emit('save', [lightTheme, darkTheme])
}
</script>

<style scoped>
.custom-theme-creator {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    background: var(--bg-primary);
    z-index: 100;
    display: flex;
    flex-direction: column;
}

.creator-content {
    padding: 24px;
    overflow-y: auto;
    flex: 1;
}

.form-group {
    margin-bottom: 16px;
}

.form-group label {
    display: block;
    margin-bottom: 6px;
    font-size: 13px;
    font-weight: 500;
}

.required-star {
    color: #e53e3e;
    margin-right: 2px;
}

.form-group input[type="text"] {
    width: 100%;
    padding: 8px;
    border: 1px solid var(--border-color);
    border-radius: 4px;
    background: var(--bg-secondary);
    font-size: 14px;
}

.color-picker-section {
    margin: 20px 0;
    padding: 16px;
    background: var(--bg-secondary);
    border-radius: 6px;
}

.color-picker-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-bottom: 12px;
}

.color-picker-row:last-child {
    margin-bottom: 0;
}

.color-picker-row label {
    font-size: 14px;
    font-weight: 500;
}

.color-picker-row input[type="color"] {
    width: 60px;
    height: 40px;
    border: 1px solid var(--border-color);
    border-radius: 4px;
    cursor: pointer;
}

.preview-section {
    margin: 16px 0;
}

.preview-label {
    font-size: 13px;
    font-weight: 500;
    margin-bottom: 8px;
}

.actions {
    display: flex;
    gap: 8px;
    padding: 16px 24px;
    border-top: 1px solid var(--border-color);
    flex-shrink: 0;
}

.save-btn,
.cancel-btn {
    flex: 1;
    padding: 10px;
    border: none;
    border-radius: 4px;
    font-size: 14px;
    cursor: pointer;
    font-weight: 500;
}

.save-btn {
    background: var(--accent-color);
    color: white;
}

.save-btn:disabled {
    opacity: 0.5;
    cursor: not-allowed;
}

.cancel-btn {
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
}
</style>
