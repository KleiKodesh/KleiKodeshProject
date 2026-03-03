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
                <label>התחל מערכת נושא קיימת (אופציונלי)</label>
                <ThemePreviewDropdown v-model="baseTheme"
                                      placeholder="התחל מאפס"
                                      :show-custom-themes="false"
                                      @update:model-value="loadBaseTheme" />
            </div>

            <div class="section-divider"></div>

            <!-- Reading Background Mode Selection -->
            <div class="form-group">
                <label class="bold">רקע קריאה (אופציונלי)</label>
                <div class="button-group">
                    <button :class="['toggle-btn', { active: readingMode === 'same' }]"
                            @click="readingMode = 'same'">
                        זהה לממשק
                    </button>
                    <button :class="['toggle-btn', { active: readingMode === 'preset' }]"
                            @click="readingMode = 'preset'">
                        רקע מוכן
                    </button>
                    <button :class="['toggle-btn', { active: readingMode === 'custom' }]"
                            @click="readingMode = 'custom'">
                        רקע מותאם אישית
                    </button>
                </div>

                <ReadingBackgroundDropdown v-if="readingMode === 'preset'"
                                           v-model="presetReadingBackground"
                                           class="reading-dropdown" />

                <div v-else-if="readingMode === 'same'"
                     class="info-message">
                    רקע הקריאה יחושב באופן אוטומטי לפי צבעי הממשק שבחרת
                </div>
            </div>

            <div class="section-divider"></div>

            <!-- Color Pickers Section -->
            <div class="color-picker-section">
                <div class="section-header">צבעים</div>

                <!-- UI Colors -->
                <div class="color-picker-row">
                    <label>רקע ממשק</label>
                    <input type="color"
                           v-model="backgroundColor" />
                </div>
                <div class="color-picker-row">
                    <label>טקסט ממשק</label>
                    <input type="color"
                           v-model="textColor" />
                </div>
                <div class="color-picker-row">
                    <label>הדגשה</label>
                    <input type="color"
                           v-model="accentColor" />
                </div>

                <!-- Custom Reading Colors (only shown when readingMode === 'custom') -->
                <template v-if="readingMode === 'custom'">
                    <div class="color-picker-row">
                        <label>רקע קריאה (אופציונלי)</label>
                        <input type="color"
                               v-model="customReadingBg" />
                    </div>
                    <div class="color-picker-row">
                        <label>טקסט קריאה (אופציונלי)</label>
                        <input type="color"
                               v-model="customReadingText" />
                    </div>
                </template>
            </div>

            <div class="preview-section">
                <div class="preview-label">תצוגה מקדימה - ממשק</div>
                <ThemePreviewPair :light-colors="lightUiVariantColors"
                                  :dark-colors="darkUiVariantColors"
                                  :active-variant="currentVariant"
                                  :interactive="true"
                                  @click:light="loadLightVariant"
                                  @click:dark="loadDarkVariant" />
            </div>

            <div v-if="readingMode !== 'same'"
                 class="preview-section">
                <div class="preview-label">תצוגה מקדימה - רקע קריאה</div>
                <ThemePreviewPair :light-colors="lightVariantColors"
                                  :dark-colors="darkVariantColors"
                                  :active-variant="currentVariant"
                                  :interactive="false" />
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
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { THEME_PRESETS, getTheme, type ThemePreset, type ThemeColors } from '@/utils/themes'
import type { ReadingBackgroundPreset } from '@/utils/readingBackgrounds'
import ThemePreviewDropdown from './ThemePreviewDropdown.vue'
import ThemePreviewPair from './ThemePreviewPair.vue'
import ReadingBackgroundDropdown from './ReadingBackgroundDropdown.vue'

const emit = defineEmits<{
    close: []
    save: [themes: Array<{ id: string; name: string; isDark: boolean; reading: ThemeColors; ui: ThemeColors }>]
}>()

// Handle Escape key to close dialog
const handleEscape = (event: KeyboardEvent) => {
    if (event.key === 'Escape') {
        emit('close')
    }
}

onMounted(() => {
    window.addEventListener('keydown', handleEscape)
})

onUnmounted(() => {
    window.removeEventListener('keydown', handleEscape)
})

const themeName = ref('')
const baseTheme = ref<ThemePreset | ''>('')
const currentVariant = ref<'light' | 'dark'>('light')

// Reading background mode: 'same' (use UI colors), 'preset' (use existing theme), or 'custom' (define own colors)
const readingMode = ref<'same' | 'preset' | 'custom'>('same')
const presetReadingBackground = ref<ReadingBackgroundPreset>('default')

// Custom reading colors (only used when readingMode === 'custom')
const customReadingBg = ref('#ffffff')
const customReadingText = ref('#1f1f1f')

// UI colors - always custom
const backgroundColor = ref('#ffffff')
const textColor = ref('#1f1f1f')
const accentColor = ref('#0078d4')

const loadBaseTheme = () => {
    if (!baseTheme.value) return
    const theme = THEME_PRESETS[baseTheme.value]
    if (theme) {
        // Load UI colors
        backgroundColor.value = theme.ui.bgPrimary
        textColor.value = theme.ui.textPrimary
        accentColor.value = theme.ui.accentColor

        // Load reading colors into custom fields
        customReadingBg.value = theme.reading.bgPrimary
        customReadingText.value = theme.reading.textPrimary

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

// Computed reading colors based on mode
const computedReadingColors = computed((): ThemeColors => {
    if (readingMode.value === 'preset' && presetReadingBackground.value !== 'default') {
        // Use preset theme's reading colors
        const bgTheme = getTheme(presetReadingBackground.value as ThemePreset)
        if (bgTheme) {
            return bgTheme.reading
        }
    }

    if (readingMode.value === 'same') {
        // Use UI colors for reading (same as UI)
        return computedUiColors.value
    }

    // Use custom reading colors
    const isDark = isDarkTheme(customReadingBg.value)
    const alphas = adjustAlpha(isDark)

    return {
        bgPrimary: customReadingBg.value,
        bgSecondary: isDark ? lighten(customReadingBg.value, 10) : darken(customReadingBg.value, 8),
        textPrimary: customReadingText.value,
        textSecondary: isDark ? darken(customReadingText.value, 40) : lighten(customReadingText.value, 60),
        borderColor: isDark ? lighten(customReadingBg.value, 20) : darken(customReadingBg.value, 15),
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

// Light and dark variant colors for preview - UI colors
const lightUiVariantColors = computed(() => {
    const isDark = isDarkTheme(backgroundColor.value)
    return isDark ? invertColors(computedUiColors.value) : computedUiColors.value
})

const darkUiVariantColors = computed(() => {
    const isDark = isDarkTheme(backgroundColor.value)
    return isDark ? computedUiColors.value : invertColors(computedUiColors.value)
})

// Light and dark variant colors for preview - Reading colors
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

    if (readingMode.value === 'custom') {
        customReadingBg.value = colors.bgPrimary
        customReadingText.value = colors.textPrimary
    }
}

// Load dark variant colors into the form
function loadDarkVariant() {
    currentVariant.value = 'dark'
    const colors = darkVariantColors.value
    backgroundColor.value = colors.bgPrimary
    textColor.value = colors.textPrimary
    accentColor.value = colors.accentColor

    if (readingMode.value === 'custom') {
        customReadingBg.value = colors.bgPrimary
        customReadingText.value = colors.textPrimary
    }
}

// When switching from preset to custom, load the preset colors into custom fields
watch(readingMode, (newMode) => {
    if (newMode === 'custom') {
        if (presetReadingBackground.value !== 'default') {
            const bgTheme = getTheme(presetReadingBackground.value as ThemePreset)
            if (bgTheme) {
                customReadingBg.value = bgTheme.reading.bgPrimary
                customReadingText.value = bgTheme.reading.textPrimary
            }
        } else {
            // Load from UI colors
            customReadingBg.value = backgroundColor.value
            customReadingText.value = textColor.value
        }
    }
})

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

.section-header {
    font-size: 13px;
    font-weight: 600;
    margin-bottom: 12px;
    color: var(--text-primary);
}

.section-divider {
    height: 1px;
    background: var(--border-color);
    margin: 20px 0;
}

.button-group {
    display: flex;
    gap: 8px;
    margin-bottom: 12px;
}

.toggle-btn {
    flex: 1;
    padding: 8px 12px;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 6px;
    cursor: pointer;
    font-size: 13px;
    transition: all 0.15s;
}

.toggle-btn:hover {
    border-color: var(--accent-color);
}

.toggle-btn.active {
    background: var(--accent-color);
    color: white;
    border-color: var(--accent-color);
}

.reading-dropdown {
    margin-top: 8px;
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

.setting-description {
    font-size: 12px;
    color: var(--text-secondary);
    margin-bottom: 8px;
}

.section-divider {
    height: 1px;
    background: var(--border-color);
    margin: 20px 0;
}

.section-header {
    font-size: 13px;
    font-weight: 600;
    margin-bottom: 12px;
    color: var(--text-primary);
}

.button-group {
    display: flex;
    gap: 8px;
    margin-bottom: 12px;
}

.toggle-btn {
    flex: 1;
    padding: 8px 12px;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 6px;
    font-size: 13px;
    cursor: pointer;
    transition: all 0.15s;
}

.toggle-btn:hover {
    background: var(--hover-bg);
    border-color: var(--accent-color);
}

.toggle-btn.active {
    background: var(--accent-color);
    color: #fff;
    border-color: var(--accent-color);
}

.reading-dropdown {
    margin-top: 8px;
}

.custom-reading-colors {
    margin-top: 8px;
    padding: 12px;
    background: var(--bg-secondary);
    border-radius: 6px;
}

.custom-reading-colors .color-picker-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-bottom: 12px;
}

.custom-reading-colors .color-picker-row:last-child {
    margin-bottom: 0;
}

.custom-reading-colors .color-picker-row label {
    font-size: 13px;
    font-weight: 500;
}

.custom-reading-colors .color-picker-row input[type="color"] {
    width: 60px;
    height: 40px;
    border: 1px solid var(--border-color);
    border-radius: 4px;
    cursor: pointer;
}

.info-message {
    padding: 12px;
    background: var(--bg-secondary);
    border-radius: 6px;
    font-size: 13px;
    color: var(--text-secondary);
    text-align: center;
    margin-top: 8px;
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
