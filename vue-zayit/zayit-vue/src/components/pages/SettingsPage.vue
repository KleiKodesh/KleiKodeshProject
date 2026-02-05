<template>
    <div class="flex-column width-fill height-fill settings-page">
        <div class="flex-110 overflow-y settings-content">

            <!-- Database Path - only show in C# WebView mode -->
            <div v-if="webviewBridge.isAvailable()"
                 class="setting-group">
                <label class="flex-row bold setting-label">מיקום מסד הנתונים</label>
                <div class="flex-column">
                    <div class="flex-row database-path-row">
                        <input type="text"
                               v-model="databasePath"
                               placeholder="בחר מיקום מסד הנתונים (seforim.db)"
                               class="database-path-input"
                               readonly />
                        <button @click="selectDatabaseFile"
                                class="c-pointer database-browse-btn"
                                title="בחר קובץ מסד נתונים">
                            📁
                        </button>
                    </div>
                </div>
            </div>

            <!-- Header Font -->
            <div class="setting-group">
                <label class="flex-row bold setting-label">גופן
                    כותרות</label>
                <div class="flex-row c-pointer custom-select"
                     @click="toggleHeaderDropdown"
                     tabindex="0">
                    <div class="select-display">{{ getDisplayName(headerFont) }}
                    </div>
                    <div class="select-arrow">▼</div>
                    <div v-if="isHeaderDropdownOpen"
                         class="select-dropdown"
                         @click.stop>
                        <div v-for="font in availableFonts"
                             :key="font"
                             class="c-pointer select-option"
                             :class="{ selected: headerFont.includes(font) }"
                             @click="selectHeaderFont(font)">
                            {{ font }}
                        </div>
                    </div>
                </div>
            </div>

            <!-- Text Font -->
            <div class="setting-group">
                <label class="flex-row bold setting-label">גופן
                    טקסט</label>
                <div class="flex-row c-pointer custom-select"
                     @click="toggleTextDropdown"
                     tabindex="0">
                    <div class="select-display">{{ getDisplayName(textFont) }}
                    </div>
                    <div class="select-arrow">▼</div>
                    <div v-if="isTextDropdownOpen"
                         class="select-dropdown"
                         @click.stop>
                        <div v-for="font in availableFonts"
                             :key="font"
                             class="c-pointer select-option"
                             :class="{ selected: textFont.includes(font) }"
                             @click="selectTextFont(font)">
                            {{ font }}
                        </div>
                    </div>
                </div>
            </div>

            <!-- Font Size -->
            <div class="setting-group">
                <label class="flex-between bold setting-label">
                    גודל גופן
                    <span class="text-secondary setting-value">{{
                        fontSize
                        }}%</span>
                </label>
                <input type="range"
                       v-model.number="fontSize"
                       min="50"
                       max="200"
                       step="5"
                       class="setting-slider" />
            </div>

            <!-- Line Padding -->
            <div class="setting-group">
                <label class="flex-between bold setting-label">
                    ריווח שורות
                    <span class="text-secondary setting-value">{{ linePadding
                        }}</span>
                </label>
                <input type="range"
                       v-model.number="linePadding"
                       min="1.2"
                       max="3.0"
                       step="0.1"
                       class="setting-slider" />
            </div>

            <!-- App Zoom -->
            <div class="setting-group">
                <label class="flex-between bold setting-label">
                    זום האפליקציה
                    <span class="text-secondary setting-value">{{
                        Math.round(appZoom * 100) }}%</span>
                </label>
                <input type="range"
                       v-model.number="appZoom"
                       min="0.5"
                       max="1.5"
                       step="0.05"
                       class="setting-slider" />
            </div>

            <!-- Divine Name Censoring -->
            <div class="setting-group">
                <label class="flex-row  bold setting-label">כיסוי שם
                    ה'</label>
                <div class="flex-row theme-toggle">
                    <button :class="{ active: !censorDivineNames }"
                            @click="setCensorDivineNames(false)"
                            class="flex-110 c-pointer theme-option">
                        כתיב מלא
                    </button>
                    <button :class="{ active: censorDivineNames }"
                            @click="setCensorDivineNames(true)"
                            class="flex-110 c-pointer theme-option">
                        כיסוי (ה→ק)
                    </button>
                </div>
            </div>

            <!-- Homepage Preference - only show when online -->
            <div v-if="isOnline"
                 class="setting-group">
                <label class="flex-row bold setting-label">דף בית מועדף</label>
                <div class="flex-row theme-toggle">
                    <button :class="{ active: !useOfflineHomepage }"
                            @click="setUseOfflineHomepage(false)"
                            class="flex-110 c-pointer theme-option">
                        דף בית רגיל
                    </button>
                    <button :class="{ active: useOfflineHomepage }"
                            @click="setUseOfflineHomepage(true)"
                            class="flex-110 c-pointer theme-option">
                        איתור - כזית
                    </button>
                </div>
            </div>

            <!-- Reading Background Color -->
            <div class="setting-group">
                <label class="flex-row bold setting-label">רקע קריאה</label>
                <div class="flex-column">
                    <div class="color-palette">
                        <button v-for="color in readingBackgroundColors"
                                :key="color.value"
                                :class="['color-swatch', { active: readingBackgroundColor === color.value }]"
                                :style="{ backgroundColor: color.value || 'var(--bg-primary)' }"
                                :title="color.name"
                                @click="readingBackgroundColor = color.value">
                            <span v-if="!color.value"
                                  class="default-indicator">ברירת מחדל</span>
                        </button>
                    </div>
                    <div class="flex-row flex-center custom-color-row">
                        <input type="color"
                               v-model="readingBackgroundColor"
                               class="color-picker" />
                        <span class="custom-color-label">צבע מותאם אישית</span>
                        <button v-if="readingBackgroundColor && !isPresetColor(readingBackgroundColor, readingBackgroundColors)"
                                @click="readingBackgroundColor = ''"
                                class="c-pointer clear-color-btn"
                                title="נקה צבע">
                            ✕
                        </button>
                    </div>
                </div>
            </div>

            <!-- Reset Button -->
            <div class="setting-group">
                <button @click="resetSettings"
                        class="width-fill c-pointer bold reset-button">
                    איפוס הגדרות
                </button>
            </div>
        </div>

        <!-- Custom Dialog -->
        <CustomDialog ref="dialogRef"
                      :title="dialogOptions.title"
                      :message="dialogOptions.message"
                      :icon="dialogOptions.icon"
                      :icon-type="dialogOptions.iconType"
                      :confirm-text="dialogOptions.confirmText"
                      :cancel-text="dialogOptions.cancelText"
                      :confirm-variant="dialogOptions.confirmVariant"
                      :show-confirm="dialogOptions.showConfirm"
                      :show-cancel="dialogOptions.showCancel"
                      :show-close-button="dialogOptions.showCloseButton"
                      :show-actions="dialogOptions.showActions"
                      :size="dialogOptions.size"
                      :close-on-overlay="dialogOptions.closeOnOverlay"
                      @confirm="handleConfirm"
                      @cancel="handleCancel"
                      @close="handleClose" />
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '../../stores/settingsStore'
import { hebrewFonts } from '../../services/hebrewFontsService'
import { useConnectivity } from '../../utils/connectivity'
import { webviewBridge } from '../../services/webviewBridge'
import { useDialog } from '../../composables/useDialog'
import CustomDialog from '../common/CustomDialog.vue'

const settingsStore = useSettingsStore()
const { headerFont, textFont, fontSize, linePadding, censorDivineNames, appZoom, useOfflineHomepage, readingBackgroundColor, databasePath } = storeToRefs(settingsStore)
const { isOnline } = useConnectivity()
const { dialogRef, dialogOptions, confirm, error, handleConfirm, handleCancel, handleClose } = useDialog()

const availableFonts = ref<string[]>([])
const isHeaderDropdownOpen = ref(false)
const isTextDropdownOpen = ref(false)

// Reading background colors palette - based on research for optimal reading comfort
const readingBackgroundColors = [
    { name: 'ברירת מחדל', value: '' },
    { name: 'קרם חם', value: '#FDF6E3' }, // Warm cream - reduces eye strain
    { name: 'בז\' רך', value: '#F5F5DC' }, // Soft beige - classic reading background
    { name: 'נייר ישן', value: '#FAF0E6' }, // Old paper - vintage feel
    { name: 'ירוק רך', value: '#F0F8F0' }, // Soft green - calming for eyes
    { name: 'כחול רך', value: '#F0F8FF' }, // Soft blue - reduces glare
    { name: 'אפור בהיר', value: '#F8F8F8' }, // Light gray - neutral
    { name: 'ורוד רך', value: '#FFF0F5' }, // Soft pink - warm and gentle
    { name: 'צהוב עדין', value: '#FFFACD' }, // Light yellow - bright but soft
]

// Helper function to check if a color is in the preset palette
const isPresetColor = (color: string, palette: typeof readingBackgroundColors) => {
    return palette.some(preset => preset.value === color)
}

// Use imported font list
const fontsToCheck = hebrewFonts

// Function to check if a font is available
const isFontAvailable = (fontName: string): boolean => {
    const canvas = document.createElement('canvas')
    const context = canvas.getContext('2d')
    if (!context) return false

    const testString = 'אבגדהוזחטיכלמנסעפצקרשת'
    const baseFonts = ['monospace', 'sans-serif', 'serif']

    const testFont = (font: string, baseFont: string) => {
        context.font = `72px ${baseFont}`
        const baseWidth = context.measureText(testString).width

        context.font = `72px '${font}', ${baseFont}`
        const fontWidth = context.measureText(testString).width

        return baseWidth !== fontWidth
    }

    return baseFonts.some(baseFont => testFont(fontName, baseFont))
}

// Detect available fonts
const detectFonts = () => {
    const detected: string[] = []
    for (const font of fontsToCheck) {
        if (isFontAvailable(font)) {
            detected.push(font)
        }
    }
    availableFonts.value = detected.length > 0 ? detected : fontsToCheck
}

const toggleHeaderDropdown = () => {
    isHeaderDropdownOpen.value = !isHeaderDropdownOpen.value
    isTextDropdownOpen.value = false
}

const toggleTextDropdown = () => {
    isTextDropdownOpen.value = !isTextDropdownOpen.value
    isHeaderDropdownOpen.value = false
}

const selectHeaderFont = (font: string) => {
    headerFont.value = `'${font}', sans-serif`
    isHeaderDropdownOpen.value = false
}

const selectTextFont = (font: string) => {
    textFont.value = `'${font}', serif`
    isTextDropdownOpen.value = false
}

const getDisplayName = (fontValue: string): string => {
    const match = fontValue.match(/'([^']+)'/)
    return match && match[1] ? match[1] : fontValue
}

const resetSettings = async () => {
    const confirmed = await confirm(
        'האם אתה בטוח שברצונך לאפס את כל ההגדרות? פעולה זו תחזיר את האפליקציה למצב ברירת המחדל.',
        {
            title: 'איפוס הגדרות',
            confirmVariant: 'danger'
        }
    )

    if (!confirmed) {
        return
    }

    settingsStore.reset()

    // Also reset database path to default in C# mode
    if (webviewBridge.isAvailable()) {
        try {
            // Clear the custom database path (this will revert to default)
            const success = await webviewBridge.clearDatabasePath()
            if (success) {
                // Reload the page to use the default database and apply all reset settings
                window.location.reload()
                return
            } else {
                console.error('Failed to reset database path, but continuing with other settings reset')
            }
        } catch (error) {
            console.error('Failed to reset database path:', error)
        }
    }

    // If not in C# mode or database reset failed, still reload to apply other settings
    window.location.reload()
}

const setCensorDivineNames = (censor: boolean) => {
    censorDivineNames.value = censor
    // Reload to apply censoring from data layer
    window.location.reload()
}

const setUseOfflineHomepage = (useOffline: boolean) => {
    useOfflineHomepage.value = useOffline
}

const selectDatabaseFile = async () => {
    try {
        const result = await webviewBridge.openDatabaseFilePicker()
        if (result.filePath) {
            // First validate the selected database file
            const isValid = await webviewBridge.validateDatabasePath(result.filePath)
            if (!isValid) {
                await error('הקובץ שנבחר אינו מסד נתונים תקין של SQLite. אנא בחר קובץ seforim.db תקין.')
                return
            }

            databasePath.value = result.filePath
            // Notify the C# backend about the new database path
            const success = await webviewBridge.setDatabasePath(result.filePath)
            if (success) {
                // Reload the page to use the new database
                window.location.reload()
            } else {
                await error('שגיאה בהגדרת מיקום מסד הנתונים. אנא נסה שוב.')
                databasePath.value = '' // Reset on failure
            }
        }
    } catch (err) {
        console.error('Failed to select database file:', err)
        await error('שגיאה בבחירת קובץ מסד הנתונים. אנא נסה שוב.')
    }
}

const loadCurrentDatabasePath = async () => {
    try {
        if (webviewBridge.isAvailable()) {
            // C# mode - get path from backend
            const currentPath = await webviewBridge.getCurrentDatabasePath()
            if (currentPath && !databasePath.value) {
                databasePath.value = currentPath
            }
        }
    } catch (error) {
        console.error('Failed to load current database path:', error)
    }
}

onMounted(() => {
    detectFonts()
    // Load database path only in C# WebView mode
    if (webviewBridge.isAvailable()) {
        loadCurrentDatabasePath()
    }
})
</script>

<style scoped>
.settings-page {
    background: var(--bg-primary);
}

.settings-content {
    padding: 20px;
}

.setting-group {
    margin-bottom: 24px;
}

.setting-label {
    font-size: 14px;
    color: var(--text-primary);
    margin-bottom: 8px;
}

.setting-value {
    font-size: 13px;
}

.custom-select {
    position: relative;
    width: 100%;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 4px;
    direction: rtl;
    height: 38px;
    padding: 0 12px;
    user-select: none;
}

.custom-select:hover {
    border-color: var(--accent-color);
}

.select-display {
    flex: 1;
    color: var(--text-primary);
    font-size: 14px;
}

.select-arrow {
    font-size: 10px;
    color: var(--text-secondary);
    margin-left: 8px;
}

.select-dropdown {
    position: absolute;
    top: 100%;
    left: 0;
    right: 0;
    margin-top: 4px;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 4px;
    max-height: 200px;
    overflow-y: auto;
    z-index: 1001;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

.select-option {
    padding: 8px 12px;
    color: var(--text-primary);
    font-size: 13px;
}

.select-option:hover {
    background: var(--hover-bg);
}

.select-option.selected {
    background: var(--accent-bg);
    color: var(--accent-color);
    font-weight: 500;
}

.setting-slider {
    width: 100%;
    height: 6px;
    background: var(--bg-secondary);
    border-radius: 3px;
    outline: none;
    -webkit-appearance: none;
    appearance: none;
}

.setting-slider::-webkit-slider-thumb {
    -webkit-appearance: none;
    appearance: none;
    width: 18px;
    height: 18px;
    background: var(--accent-color);
    border-radius: 50%;
    cursor: pointer;
    transition: all 0.2s ease;
}

.setting-slider::-moz-range-thumb {
    width: 18px;
    height: 18px;
    background: var(--accent-color);
    border-radius: 50%;
    cursor: pointer;
    border: none;
    transition: all 0.2s ease;
}

.theme-toggle {
    gap: 8px;
}

.theme-option {
    padding: 10px;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 4px;
    color: var(--text-primary);
    font-size: 14px;
}

.theme-option:hover {
    background: var(--hover-bg);
    border-color: var(--accent-color);
}

.theme-option.active {
    background: var(--accent-color);
    color: white;
    border-color: var(--accent-color);
}

.reset-button {
    padding: 12px;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 4px;
    color: var(--text-primary);
    font-size: 14px;
}

.reset-button:hover {
    background: var(--hover-bg);
    border-color: var(--accent-color);
}

/* Color palette styles */
.color-palette {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
    margin-bottom: 12px;
}

.color-swatch {
    width: 40px;
    height: 40px;
    border: 2px solid var(--border-color);
    border-radius: 6px;
    cursor: pointer;
    position: relative;
    transition: all 0.2s ease;
    display: flex;
    align-items: center;
    justify-content: center;
    user-select: none;
}

.color-swatch:hover {
    border-color: var(--accent-color);
    transform: scale(1.05);
}

.color-swatch.active {
    border-color: var(--accent-color);
    border-width: 3px;
    box-shadow: 0 0 0 2px var(--accent-bg);
}

.default-indicator {
    font-size: 10px;
    color: var(--text-secondary);
    text-align: center;
    line-height: 1.2;
    font-weight: bold;
}

.custom-color-row {
    gap: 8px;
    align-items: center;
    margin-top: 8px;
}

.custom-color-label {
    font-size: 13px;
    color: var(--text-secondary);
    flex: 1;
}

/* Color picker styles */
.color-picker {
    width: 40px;
    height: 40px;
    border: 2px solid var(--border-color);
    border-radius: 6px;
    cursor: pointer;
    background: none;
    padding: 0;
}

.color-picker:hover {
    border-color: var(--accent-color);
}

.clear-color-btn {
    width: 24px;
    height: 24px;
    border: 1px solid var(--border-color);
    border-radius: 50%;
    background: var(--bg-secondary);
    color: var(--text-secondary);
    font-size: 12px;
    display: flex;
    align-items: center;
    justify-content: center;
    user-select: none;
}

.clear-color-btn:hover {
    background: var(--hover-bg);
    border-color: var(--accent-color);
    color: var(--text-primary);
}

/* Database path styles - only visible in C# mode */
.database-path-row {
    gap: 8px;
    align-items: center;
}

.database-path-input {
    flex: 1;
    padding: 8px 12px;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 4px;
    color: var(--text-primary);
    font-size: 14px;
    direction: ltr;
    text-align: left;
    cursor: pointer;
}

.database-path-input:hover {
    border-color: var(--accent-color);
}

.database-browse-btn {
    width: 38px;
    height: 38px;
    background: var(--accent-color);
    border: none;
    border-radius: 4px;
    color: white;
    font-size: 16px;
    display: flex;
    align-items: center;
    justify-content: center;
    transition: all 0.2s ease;
}

.database-browse-btn:hover {
    background: var(--accent-hover);
    transform: scale(1.05);
}
</style>
