<template>
    <div class="flex-column width-fill height-fill settings-page">

        <!-- Tab Bar: all items horizontally stacked -->
        <div class="flex-row tab-bar">
            <template v-if="!showCustomThemeCreator">
                <button :class="['tab-btn flex-center', { active: activeTab === 'general' }]"
                        @click="activeTab = 'general'">
                    כללי
                </button>
                <button :class="['tab-btn flex-center', { active: activeTab === 'reading' }]"
                        @click="activeTab = 'reading'">
                    קריאה
                </button>
                <button class="tab-btn tab-btn--reset flex-center"
                        @click="resetSettings">
                    ↺ איפוס
                </button>
            </template>
            <template v-else>
                <div class="tab-btn flex-center active theme-creator-title">
                    יצירת ערכת נושא
                </div>
            </template>
        </div>

        <!-- Tab Content -->
        <div class="flex-110 overflow-y settings-content">

            <!-- ══ TAB: תצוגה — fonts, zoom, theme, background ══ -->
            <div v-if="activeTab === 'reading'"
                 class="tab-pane">

                <div class="setting-group">
                    <label class="setting-label flex-between bold">גופן כותרות</label>
                    <div class="c-pointer custom-select flex-row"
                         ref="headerDropdownRef"
                         @click="toggleHeaderDropdown"
                         tabindex="0">
                        <div class="select-display">{{ getDisplayName(headerFont) }}</div>
                        <div class="select-arrow">{{ isHeaderDropdownOpen ? '▲' : '▼' }}</div>
                        <div v-if="isHeaderDropdownOpen"
                             class="select-dropdown"
                             :style="headerDropdownStyles"
                             @click.stop>
                            <div v-for="font in availableFonts"
                                 :key="font"
                                 class="c-pointer select-option"
                                 :class="{ selected: headerFont.includes(font) }"
                                 @click="selectHeaderFont(font)">{{ font }}
                            </div>
                        </div>
                    </div>
                </div>

                <div class="setting-group">
                    <label class="setting-label flex-between bold">גופן טקסט</label>
                    <div class="c-pointer custom-select flex-row"
                         ref="textDropdownRef"
                         @click="toggleTextDropdown"
                         tabindex="0">
                        <div class="select-display">{{ getDisplayName(textFont) }}</div>
                        <div class="select-arrow">{{ isTextDropdownOpen ? '▲' : '▼' }}</div>
                        <div v-if="isTextDropdownOpen"
                             class="select-dropdown"
                             :style="textDropdownStyles"
                             @click.stop>
                            <div v-for="font in availableFonts"
                                 :key="font"
                                 class="c-pointer select-option"
                                 :class="{ selected: textFont.includes(font) }"
                                 @click="selectTextFont(font)">{{ font }}</div>
                        </div>
                    </div>
                </div>

                <div class="setting-group">
                    <label class="setting-label flex-between">
                        <span class="bold">גודל גופן</span>
                        <span class="text-secondary setting-value">{{ fontSize }}%</span>
                    </label>
                    <input type="range"
                           v-model.number="fontSize"
                           min="50"
                           max="200"
                           step="5"
                           class="setting-slider" />
                </div>

                <div class="setting-group">
                    <label class="setting-label flex-between">
                        <span class="bold">ריווח שורות</span>
                        <span class="text-secondary setting-value">{{ linePadding }}</span>
                    </label>
                    <input type="range"
                           v-model.number="linePadding"
                           min="1.2"
                           max="3.0"
                           step="0.1"
                           class="setting-slider" />
                </div>

                <div class="setting-group">
                    <label class="setting-label flex-between bold">רקע קריאה</label>
                    <ReadingBackgroundDropdown v-model="readingBackground" />
                </div>

            </div>

            <!-- ══ TAB: כללי — behaviour, navigation, advanced ══ -->
            <div v-if="activeTab === 'general'"
                 class="tab-pane">

                <div class="setting-group">
                    <label class="setting-label bold">ערכת נושא</label>
                    <ThemePreviewDropdown v-model="themePreset"
                                          :show-custom-themes="true"
                                          :show-delete="true"
                                          @delete="deleteCustomThemeHandler" />
                    <button @click="openCustomThemeCreator"
                            class="create-theme-btn">
                        צור ערכת נושא חדשה
                    </button>
                </div>

                <div class="setting-group">
                    <label class="setting-label flex-between">
                        <span class="bold">זום האפליקציה</span>
                        <span class="text-secondary setting-value">{{ Math.round(appZoom * 100) }}%</span>
                    </label>
                    <input type="range"
                           v-model.number="appZoom"
                           min="0.5"
                           max="1.5"
                           step="0.05"
                           class="setting-slider" />
                </div>

                <div class="setting-group">
                    <label class="setting-label flex-between bold">דף ברירת מחדל לטאב חדש</label>
                    <div class="button-group flex-row wrap">
                        <button :class="['toggle-btn compact c-pointer', { active: newTabPage === 'homepage' }]"
                                @click="newTabPage = 'homepage'">דף הבית</button>
                        <button :class="['toggle-btn compact c-pointer', { active: newTabPage === 'openfile' }]"
                                @click="newTabPage = 'openfile'">פתיחת ספר</button>
                        <button :class="['toggle-btn compact c-pointer', { active: newTabPage === 'hebrewbooks' }]"
                                @click="newTabPage = 'hebrewbooks'">היברו בוקס</button>
                        <button :class="['toggle-btn compact c-pointer', { active: newTabPage === 'kezayit-search' }]"
                                @click="newTabPage = 'kezayit-search'">חיפוש</button>
                    </div>
                </div>

                <div class="setting-group">
                    <label class="setting-label flex-between bold">מיקום ברירת מחדל של סרגל הכלים</label>
                    <div class="button-group flex-row wrap">
                        <button :class="['toggle-btn compact c-pointer', { active: defaultBookViewToolbarPosition === 'top' }]"
                                @click="defaultBookViewToolbarPosition = 'top'">למעלה</button>
                        <button :class="['toggle-btn compact c-pointer', { active: defaultBookViewToolbarPosition === 'bottom' }]"
                                @click="defaultBookViewToolbarPosition = 'bottom'">למטה</button>
                        <button :class="['toggle-btn compact c-pointer', { active: defaultBookViewToolbarPosition === 'left' }]"
                                @click="defaultBookViewToolbarPosition = 'left'">שמאל</button>
                        <button :class="['toggle-btn compact c-pointer', { active: defaultBookViewToolbarPosition === 'right' }]"
                                @click="defaultBookViewToolbarPosition = 'right'">ימין</button>
                        <button :class="['toggle-btn compact c-pointer', { active: defaultBookViewToolbarPosition === 'float-vertical' }]"
                                @click="defaultBookViewToolbarPosition = 'float-vertical'">צף מאונך</button>
                        <button :class="['toggle-btn compact c-pointer', { active: defaultBookViewToolbarPosition === 'float-horizontal' }]"
                                @click="defaultBookViewToolbarPosition = 'float-horizontal'">צף מאוזן</button>
                    </div>
                </div>

                <div class="setting-group">
                    <label class="setting-label flex-between bold">מצב טעמים וניקוד</label>
                    <div class="button-group flex-row">
                        <button :class="['toggle-btn c-pointer', { active: !globalDiacritics }]"
                                @click="globalDiacritics = false">לכל טאב בנפרד</button>
                        <button :class="['toggle-btn c-pointer', { active: globalDiacritics }]"
                                @click="globalDiacritics = true">גלובלי</button>
                    </div>
                </div>

                <div class="setting-group">
                    <label class="setting-label flex-between bold">כיסוי שם ה'</label>
                    <div class="button-group flex-row">
                        <button :class="['toggle-btn c-pointer', { active: !censorDivineNames }]"
                                @click="setCensorDivineNames(false)">כתיב מלא</button>
                        <button :class="['toggle-btn c-pointer', { active: censorDivineNames }]"
                                @click="setCensorDivineNames(true)">כיסוי (ה→ק)</button>
                    </div>
                </div>

                <div v-if="webviewBridge.isAvailable()"
                     class="setting-group">
                    <label class="setting-label flex-between bold">מיקום מסד הנתונים</label>
                    <div class="database-path-row flex-row">
                        <input type="text"
                               v-model="databasePath"
                               placeholder="בחר מיקום מסד הנתונים (seforim.db)"
                               class="database-path-input"
                               readonly />
                        <button @click="selectDatabaseFile"
                                class="c-pointer database-browse-btn flex-center">📁</button>
                    </div>
                </div>

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

        <!-- Custom Theme Creator Overlay -->
        <div v-if="showCustomThemeCreator"
             class="theme-creator-overlay">
            <ThemeCreator @close="closeCustomThemeCreator"
                          @save="handleCustomThemeSave" />
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '../../stores/settingsStore'
import type { SettingsTab } from '../../stores/settingsStore'
import { hebrewFonts } from '../../utils/hebrewFonts'
import { webviewBridge } from '../../services/webviewBridge'
import { useDialog } from '../../composables/useDialog'
import { useDropdownPosition } from '../../composables/useDropdownPosition'
import CustomDialog from '../common/CustomDialog.vue'
import ThemePreviewDropdown from '../ThemePreviewDropdown.vue'
import ThemeCreator from '../ThemeCreator.vue'
import ReadingBackgroundDropdown from '../ReadingBackgroundDropdown.vue'
import { getReadingBackgrounds } from '../../config/readingBackgrounds'
import { addCustomTheme, deleteCustomTheme, getTheme, type ThemePreset, type ThemeColors } from '../../config/themes'

const settingsStore = useSettingsStore()
const {
    headerFont, textFont, fontSize, linePadding, censorDivineNames,
    appZoom, readingBackground, databasePath, globalDiacritics,
    newTabPage, defaultBookViewToolbarPosition, themePreset, lastSettingsTab
} = storeToRefs(settingsStore)
const { dialogRef, dialogOptions, confirm, error, handleConfirm, handleCancel, handleClose } = useDialog()

const activeTab = ref<SettingsTab>(lastSettingsTab.value)
const availableFonts = ref<string[]>([])
const isHeaderDropdownOpen = ref(false)
const isTextDropdownOpen = ref(false)
const showCustomThemeCreator = ref(false)
const headerDropdownRef = ref<HTMLElement>()
const textDropdownRef = ref<HTMLElement>()

// Dropdown positioning
const { dropdownStyles: headerDropdownStyles, updatePosition: updateHeaderPosition } = useDropdownPosition(headerDropdownRef, isHeaderDropdownOpen)
const { dropdownStyles: textDropdownStyles, updatePosition: updateTextPosition } = useDropdownPosition(textDropdownRef, isTextDropdownOpen)

// Watch dropdown states to update positions
watch(isHeaderDropdownOpen, () => {
    updateHeaderPosition()
})

watch(isTextDropdownOpen, () => {
    updateTextPosition()
})

// Watch activeTab and update lastSettingsTab in store
watch(activeTab, (newTab) => {
    lastSettingsTab.value = newTab
})

const isFontAvailable = (fontName: string): boolean => {
    const canvas = document.createElement('canvas')
    const ctx = canvas.getContext('2d')
    if (!ctx) return false
    const str = 'אבגדהוזחטיכלמנסעפצקרשת'
    return ['monospace', 'sans-serif', 'serif'].some(base => {
        ctx.font = `72px ${base}`
        const w = ctx.measureText(str).width
        ctx.font = `72px '${fontName}', ${base}`
        return w !== ctx.measureText(str).width
    })
}

const detectFonts = () => {
    const detected = hebrewFonts.filter(isFontAvailable)
    availableFonts.value = detected.length > 0 ? detected : hebrewFonts
}

const toggleHeaderDropdown = () => { isHeaderDropdownOpen.value = !isHeaderDropdownOpen.value; isTextDropdownOpen.value = false }
const toggleTextDropdown = () => { isTextDropdownOpen.value = !isTextDropdownOpen.value; isHeaderDropdownOpen.value = false }
const selectHeaderFont = (font: string) => { headerFont.value = `'${font}', sans-serif`; isHeaderDropdownOpen.value = false }
const selectTextFont = (font: string) => { textFont.value = `'${font}', serif`; isTextDropdownOpen.value = false }
const getDisplayName = (v: string) => v.match(/'([^']+)'/)?.[1] ?? v

const resetSettings = async () => {
    const confirmed = await confirm(
        'האם אתה בטוח שברצונך לאפס את כל ההגדרות? פעולה זו תחזיר את האפליקציה למצב ברירת המחדל.',
        { title: 'איפוס הגדרות', confirmVariant: 'danger' }
    )
    if (!confirmed) return
    settingsStore.reset()
    if (webviewBridge.isAvailable()) { try { await webviewBridge.clearDatabasePath() } catch { } }
    window.location.reload()
}

const readingBackgrounds = getReadingBackgrounds()

const setCensorDivineNames = (censor: boolean) => { censorDivineNames.value = censor; window.location.reload() }

const selectDatabaseFile = async () => {
    try {
        const result = await webviewBridge.openDatabaseFilePicker()
        if (!result.filePath) return
        const isValid = await webviewBridge.validateDatabasePath(result.filePath)
        if (!isValid) { await error('הקובץ שנבחר אינו מסד נתונים תקין של SQLite.'); return }
        databasePath.value = result.filePath
        const ok = await webviewBridge.setDatabasePath(result.filePath)
        if (ok) window.location.reload()
        else { await error('שגיאה בהגדרת מיקום מסד הנתונים. אנא נסה שוב.'); databasePath.value = '' }
    } catch { await error('שגיאה בבחירת קובץ מסד הנתונים. אנא נסה שוב.') }
}

// Theme creator functions
function openCustomThemeCreator() {
    showCustomThemeCreator.value = true
}

function closeCustomThemeCreator() {
    showCustomThemeCreator.value = false
}

function handleCustomThemeSave(themes: Array<{ id: string; name: string; isDark: boolean; reading: ThemeColors; ui: ThemeColors }>) {
    themes.forEach(themeData => {
        const theme = {
            name: themeData.name,
            isDark: themeData.isDark,
            family: themeData.id.replace(/-light$|-dark$/, ''),
            reading: themeData.reading,
            ui: themeData.ui
        }
        addCustomTheme(themeData.id, theme)
    })

    if (themes.length > 0 && themes[0]) {
        themePreset.value = themes[0].id as ThemePreset
    }
    closeCustomThemeCreator()
}

async function deleteCustomThemeHandler(id: string) {
    const confirmed = await confirm(`האם למחוק את ערכת הנושא "${getTheme(id as ThemePreset)?.name}"?`)
    if (confirmed) {
        deleteCustomTheme(id)
        if (themePreset.value === id) {
            themePreset.value = 'fluent-light'
        }
    }
}

onMounted(() => {
    detectFonts()
    if (webviewBridge.isAvailable()) {
        webviewBridge.getCurrentDatabasePath()
            .then(p => { if (p && !databasePath.value) databasePath.value = p })
            .catch(() => { })
    }
})
</script>

<style scoped>
/* Minimal settings-specific styles - most styling comes from global CSS */

.settings-page {
    background: var(--reading-bg);
    position: relative;
}

/* Tab Bar */
.tab-bar {
    border-bottom: 1px solid var(--border-color);
    background: var(--bg-secondary);
    flex-shrink: 0;
    gap: 0;
}

.tab-btn {
    flex: 1;
    padding: 10px 6px;
    background: none;
    border: none;
    border-radius: 0;
    color: var(--text-secondary);
    font-size: 0.875rem;
    white-space: nowrap;
    transition: color 0.15s, background 0.15s;
}

.tab-btn:hover {
    color: var(--text-primary);
    background: var(--hover-bg);
}

.tab-btn.active {
    background: var(--hover-bg);
    color: var(--text-primary);
    font-weight: 700;
}

.tab-btn--reset:hover {
    color: #e53e3e;
    background: color-mix(in srgb, #e53e3e 8%, transparent);
}

.theme-creator-title {
    flex: 1;
    font-size: 1rem;
}

/* Content */
.settings-content {
    padding: 0;
}

.tab-pane {
    direction: rtl;
}

/* Setting Groups */
.setting-group {
    padding: 14px 16px;
    border-bottom: 1px solid var(--border-color);
}

.setting-group:last-child {
    border-bottom: none;
}

.setting-label {
    font-size: 14px;
    margin-bottom: 10px;
}

.setting-value {
    font-size: 13px;
    font-weight: normal;
}

/* Toggle Buttons */
.button-group {
    gap: 8px;
}

.button-group.wrap {
    flex-wrap: wrap;
}

.toggle-btn {
    flex: 1;
    padding: 10px;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 8px;
    font-size: 13px;
    transition: all 0.15s;
}

.toggle-btn.compact {
    padding: 8px 10px;
    font-size: 12px;
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

/* Custom Select */
.custom-select {
    position: relative;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 8px;
    direction: rtl;
    height: 42px;
    padding: 0 12px;
    user-select: none;
}

.custom-select:hover {
    border-color: var(--accent-color);
}

.select-display {
    flex: 1;
    font-size: 14px;
}

.select-arrow {
    font-size: 10px;
    color: var(--text-secondary);
    margin-left: 8px;
}

.select-dropdown {
    position: absolute;
    left: 0;
    right: 0;
    margin-top: 4px;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 8px;
    overflow-y: auto;
    z-index: 1001;
}

.select-option {
    padding: 10px 12px;
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

/* Slider */
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
    width: 22px;
    height: 22px;
    background: var(--accent-color);
    border-radius: 50%;
    cursor: pointer;
    box-shadow: 0 1px 4px rgba(0, 0, 0, 0.2);
}

.setting-slider::-moz-range-thumb {
    width: 22px;
    height: 22px;
    background: var(--accent-color);
    border-radius: 50%;
    cursor: pointer;
    border: none;
    box-shadow: 0 1px 4px rgba(0, 0, 0, 0.2);
}

/* Color Palette */
.color-palette {
    flex-wrap: wrap;
    gap: 8px;
    margin-bottom: 10px;
}

.color-swatch {
    width: 36px;
    height: 36px;
    border: 2px solid var(--border-color);
    border-radius: 8px;
    transition: all 0.15s;
}

.color-swatch:hover {
    border-color: var(--accent-color);
    transform: scale(1.08);
}

.color-swatch.active {
    border-color: var(--accent-color);
    border-width: 2.5px;
    box-shadow: 0 0 0 2px var(--accent-bg);
}

.default-indicator {
    font-size: 8px;
    text-align: center;
    line-height: 1.1;
    font-weight: bold;
}

.custom-color-row {
    gap: 8px;
    margin-top: 6px;
}

.custom-color-label {
    font-size: 12px;
    flex: 1;
}

.color-picker {
    width: 36px;
    height: 36px;
    border: 2px solid var(--border-color);
    border-radius: 8px;
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
    font-size: 11px;
}

.clear-color-btn:hover {
    background: var(--hover-bg);
    border-color: var(--accent-color);
}

/* Database Path */
.database-path-row {
    gap: 8px;
}

.database-path-input {
    flex: 1;
    padding: 10px 12px;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 8px;
    font-size: 13px;
    direction: ltr;
    text-align: left;
}

.database-path-input:hover {
    border-color: var(--accent-color);
}

.database-browse-btn {
    width: 42px;
    height: 42px;
    flex-shrink: 0;
    background: var(--accent-color);
    border: none;
    border-radius: 8px;
    color: #fff;
    font-size: 16px;
    transition: all 0.15s;
}

.database-browse-btn:hover {
    transform: scale(1.05);
}

/* Theme Creator Overlay */
.theme-creator-overlay {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    z-index: 1000;
}

/* Create Theme Button */
.create-theme-btn {
    width: 100%;
    padding: 10px 12px;
    background: var(--accent-color);
    color: white;
    border: none;
    border-radius: 8px;
    cursor: pointer;
    font-size: 14px;
    font-weight: 500;
    transition: opacity 0.2s ease;
    margin-top: 8px;
}

.create-theme-btn:hover {
    opacity: 0.9;
}
</style>