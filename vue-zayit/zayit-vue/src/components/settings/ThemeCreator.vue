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

            <!-- Mix and Match Option -->
            <div class="form-group">
                <label>מצב עריכה</label>
                <div class="button-group">
                    <button :class="['toggle-btn', { active: !mixAndMatch }]"
                            @click="mixAndMatch = false">
                        אוטומטי
                    </button>
                    <button :class="['toggle-btn', { active: mixAndMatch }]"
                            @click="mixAndMatch = true">
                        בהיר וכהה בנפרד
                    </button>
                </div>
                <div v-if="mixAndMatch"
                     class="info-message">
                    לחץ על התצוגה המקדימה למטה כדי לעבור בין עריכת בהיר לכהה
                </div>
                <div v-else
                     class="info-message">
                    הגרסה הכהה תיווצר אוטומטית עם צבעים מותאמים למצב כהה
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
                <div class="preview-label">
                    תצוגה מקדימה - ממשק
                    <span v-if="mixAndMatch"
                          class="clickable-hint">(לחץ לעריכה)</span>
                </div>
                <ThemePreviewPair :light-colors="lightUiVariantColors"
                                  :dark-colors="darkUiVariantColors"
                                  :active-variant="currentVariant"
                                  :interactive="mixAndMatch"
                                  @click:light="loadLightVariant"
                                  @click:dark="loadDarkVariant" />
            </div>

            <div v-if="readingMode !== 'same'"
                 class="preview-section">
                <div class="preview-label">
                    תצוגה מקדימה - רקע קריאה
                    <span v-if="mixAndMatch"
                          class="clickable-hint">(לחץ לעריכה)</span>
                </div>
                <ThemePreviewPair :light-colors="lightVariantColors"
                                  :dark-colors="darkVariantColors"
                                  :active-variant="currentVariant"
                                  :interactive="mixAndMatch"
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
import { onMounted, onUnmounted } from 'vue'
import type { ThemeColors } from '@/utils/themes'
import ThemePreviewDropdown from './ThemePreviewDropdown.vue'
import ThemePreviewPair from './ThemePreviewPair.vue'
import ReadingBackgroundDropdown from './ReadingBackgroundDropdown.vue'
import { useThemeBuilder } from './useThemeBuilder'

const emit = defineEmits<{
    close: []
    save: [themes: Array<{ id: string; name: string; isDark: boolean; reading: ThemeColors; ui: ThemeColors }>]
}>()

// Use theme builder composable
const {
    themeName,
    baseTheme,
    currentVariant,
    mixAndMatch,
    readingMode,
    presetReadingBackground,
    backgroundColor,
    textColor,
    accentColor,
    customReadingBg,
    customReadingText,
    lightUiVariantColors,
    darkUiVariantColors,
    lightVariantColors,
    darkVariantColors,
    loadBaseTheme,
    loadLightVariant,
    loadDarkVariant,
    buildThemes
} = useThemeBuilder()

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

const saveTheme = () => {
    if (!themeName.value.trim()) return
    const themes = buildThemes()
    emit('save', themes)
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

.clickable-hint {
    font-size: 12px;
    color: var(--text-secondary);
    font-weight: 400;
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
