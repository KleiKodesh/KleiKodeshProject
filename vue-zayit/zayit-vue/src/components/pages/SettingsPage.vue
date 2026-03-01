<template>
    <div class="flex-column width-fill height-fill settings-page">

        <!-- Tab Bar: all items horizontally stacked -->
        <div class="tab-bar">
            <button :class="['tab-btn', { active: activeTab === 'reading' }]"
                    @click="activeTab = 'reading'">
                קריאה
            </button>
            <button :class="['tab-btn', { active: activeTab === 'general' }]"
                    @click="activeTab = 'general'">
                כללי
            </button>
            <button class="tab-btn tab-btn--reset"
                    @click="resetSettings">
                ↺ איפוס
            </button>
        </div>

        <!-- Tab Content -->
        <div class="flex-110 overflow-y settings-content">

            <!-- ══ TAB: תצוגה — fonts, zoom, theme, background ══ -->
            <div v-if="activeTab === 'reading'"
                 class="tab-pane">

                <div class="setting-group">
                    <label class="setting-label bold">גופן כותרות</label>
                    <div class="c-pointer custom-select"
                         @click="toggleHeaderDropdown"
                         tabindex="0">
                        <div class="select-display">{{ getDisplayName(headerFont) }}</div>
                        <div class="select-arrow">▼</div>
                        <div v-if="isHeaderDropdownOpen"
                             class="select-dropdown"
                             @click.stop>
                            <div v-for="font in availableFonts"
                                 :key="font"
                                 class="c-pointer select-option"
                                 :class="{ selected: headerFont.includes(font) }"
                                 @click="selectHeaderFont(font)">{{ font }}</div>
                        </div>
                    </div>
                </div>

                <div class="setting-group">
                    <label class="setting-label bold">גופן טקסט</label>
                    <div class="c-pointer custom-select"
                         @click="toggleTextDropdown"
                         tabindex="0">
                        <div class="select-display">{{ getDisplayName(textFont) }}</div>
                        <div class="select-arrow">▼</div>
                        <div v-if="isTextDropdownOpen"
                             class="select-dropdown"
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
                    <label class="setting-label">
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
                    <label class="setting-label">
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
                    <label class="setting-label bold">רקע קריאה</label>
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
                    <div class="custom-color-row">
                        <input type="color"
                               v-model="readingBackgroundColor"
                               class="color-picker" />
                        <span class="custom-color-label">צבע מותאם אישית</span>
                        <button v-if="readingBackgroundColor && !isPresetColor(readingBackgroundColor, readingBackgroundColors)"
                                @click="readingBackgroundColor = ''"
                                class="c-pointer clear-color-btn">✕</button>
                    </div>
                </div>

            </div>

            <!-- ══ TAB: כללי — behaviour, navigation, advanced ══ -->
            <div v-if="activeTab === 'general'"
                 class="tab-pane">

                <div class="setting-group">
                    <label class="setting-label bold">ערכת נושא</label>
                    <div class="button-group">
                        <button :class="{ active: !currentTheme }"
                                @click="setTheme(false)"
                                class="toggle-btn">מצב בהיר</button>
                        <button :class="{ active: currentTheme }"
                                @click="setTheme(true)"
                                class="toggle-btn">מצב כהה</button>
                    </div>
                </div>

                <div class="setting-group">
                    <label class="setting-label">
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
                    <label class="setting-label bold">דף ברירת מחדל לטאב חדש</label>
                    <div class="button-group wrap">
                        <button :class="{ active: newTabPage === 'homepage' }"
                                @click="newTabPage = 'homepage'"
                                class="toggle-btn compact">דף הבית</button>
                        <button :class="{ active: newTabPage === 'openfile' }"
                                @click="newTabPage = 'openfile'"
                                class="toggle-btn compact">פתיחת ספר</button>
                        <button :class="{ active: newTabPage === 'hebrewbooks' }"
                                @click="newTabPage = 'hebrewbooks'"
                                class="toggle-btn compact">היברו בוקס</button>
                        <button :class="{ active: newTabPage === 'kezayit-search' }"
                                @click="newTabPage = 'kezayit-search'"
                                class="toggle-btn compact">חיפוש</button>
                    </div>
                </div>

                <div class="setting-group">
                    <label class="setting-label bold">מצב טעמים וניקוד</label>
                    <div class="button-group">
                        <button :class="{ active: !globalDiacritics }"
                                @click="globalDiacritics = false"
                                class="toggle-btn">לכל טאב בנפרד</button>
                        <button :class="{ active: globalDiacritics }"
                                @click="globalDiacritics = true"
                                class="toggle-btn">גלובלי</button>
                    </div>
                </div>

                <div class="setting-group">
                    <label class="setting-label bold">מיקום ברירת מחדל של סרגל הכלים</label>
                    <div class="button-group wrap">
                        <button :class="{ active: defaultBookViewToolbarPosition === 'top' }"
                                @click="defaultBookViewToolbarPosition = 'top'"
                                class="toggle-btn compact">למעלה</button>
                        <button :class="{ active: defaultBookViewToolbarPosition === 'bottom' }"
                                @click="defaultBookViewToolbarPosition = 'bottom'"
                                class="toggle-btn compact">למטה</button>
                        <button :class="{ active: defaultBookViewToolbarPosition === 'left' }"
                                @click="defaultBookViewToolbarPosition = 'left'"
                                class="toggle-btn compact">שמאל</button>
                        <button :class="{ active: defaultBookViewToolbarPosition === 'right' }"
                                @click="defaultBookViewToolbarPosition = 'right'"
                                class="toggle-btn compact">ימין</button>
                        <button :class="{ active: defaultBookViewToolbarPosition === 'float-vertical' }"
                                @click="defaultBookViewToolbarPosition = 'float-vertical'"
                                class="toggle-btn compact">צף מאונך</button>
                        <button :class="{ active: defaultBookViewToolbarPosition === 'float-horizontal' }"
                                @click="defaultBookViewToolbarPosition = 'float-horizontal'"
                                class="toggle-btn compact">צף מאוזן</button>
                    </div>
                </div>

                <div class="setting-group">
                    <label class="setting-label bold">כיסוי שם ה'</label>
                    <div class="button-group">
                        <button :class="{ active: !censorDivineNames }"
                                @click="setCensorDivineNames(false)"
                                class="toggle-btn">כתיב מלא</button>
                        <button :class="{ active: censorDivineNames }"
                                @click="setCensorDivineNames(true)"
                                class="toggle-btn">כיסוי (ה→ק)</button>
                    </div>
                </div>

                <div v-if="webviewBridge.isAvailable()"
                     class="setting-group">
                    <label class="setting-label bold">מיקום מסד הנתונים</label>
                    <div class="database-path-row">
                        <input type="text"
                               v-model="databasePath"
                               placeholder="בחר מיקום מסד הנתונים (seforim.db)"
                               class="database-path-input"
                               readonly />
                        <button @click="selectDatabaseFile"
                                class="c-pointer database-browse-btn">📁</button>
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
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '../../stores/settingsStore'
import { hebrewFonts } from '../../utils/hebrewFonts'
import { webviewBridge } from '../../services/webviewBridge'
import { useDialog } from '../../composables/useDialog'
import { toggleTheme, isDarkTheme, syncPdfViewerTheme } from '../../utils/theme'
import CustomDialog from '../common/CustomDialog.vue'

const settingsStore = useSettingsStore()
const {
    headerFont, textFont, fontSize, linePadding, censorDivineNames,
    appZoom, readingBackgroundColor, databasePath, globalDiacritics,
    newTabPage, defaultBookViewToolbarPosition
} = storeToRefs(settingsStore)
const { dialogRef, dialogOptions, confirm, error, handleConfirm, handleCancel, handleClose } = useDialog()

const activeTab = ref<'reading' | 'general'>('reading')
const availableFonts = ref<string[]>([])
const isHeaderDropdownOpen = ref(false)
const isTextDropdownOpen = ref(false)
const currentTheme = ref(isDarkTheme())

const readingBackgroundColors = [
    { name: 'ברירת מחדל', value: '' },
    { name: 'קרם חם', value: '#FDF6E3' },
    { name: "בז' רך", value: '#F5F5DC' },
    { name: 'נייר ישן', value: '#FAF0E6' },
    { name: 'ירוק רך', value: '#F0F8F0' },
    { name: 'כחול רך', value: '#F0F8FF' },
    { name: 'אפור בהיר', value: '#F8F8F8' },
    { name: 'ורוד רך', value: '#FFF0F5' },
    { name: 'צהוב עדין', value: '#FFFACD' },
]

const isPresetColor = (color: string, palette: typeof readingBackgroundColors) =>
    palette.some(p => p.value === color)

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

const setCensorDivineNames = (censor: boolean) => { censorDivineNames.value = censor; window.location.reload() }

const setTheme = (isDark: boolean) => {
    if (isDark !== currentTheme.value) {
        toggleTheme()
        currentTheme.value = isDarkTheme()
        setTimeout(syncPdfViewerTheme, 50)
    }
}

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
.settings-page {
    background: var(--bg-primary);
}

/* ════════════════════════════════
   Tab Bar — horizontal row
   ════════════════════════════════ */
.tab-bar {
    display: flex;
    flex-direction: row;
    /* explicit: all tabs in one horizontal line */
    border-bottom: 1px solid var(--border-color);
    background: var(--bg-secondary);
    flex-shrink: 0;
}

.tab-btn {
    flex: 1;
    display: flex;
    flex-direction: row;
    align-items: center;
    justify-content: center;
    gap: 6px;
    padding: 10px 6px;
    background: none;
    border: none;
    border-bottom: none;
    color: var(--text-secondary);
    font-size: 0.875rem;
    cursor: pointer;
    transition: color 0.15s, background 0.15s;
    white-space: nowrap;
    -webkit-tap-highlight-color: transparent;
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

/* Reset tab — distinct destructive action, never shows as "active" */
.tab-btn--reset:hover {
    color: #e53e3e;
    background: color-mix(in srgb, #e53e3e 8%, transparent);
}

/* ════════════════════════════════
   Content
   ════════════════════════════════ */
.settings-content {
    padding: 0;
}

.tab-pane {
    direction: rtl;
}

/* ════════════════════════════════
   Setting groups
   ════════════════════════════════ */
.setting-group {
    padding: 14px 16px;
    border-bottom: 1px solid var(--border-color);
}

.setting-group:last-child {
    border-bottom: none;
}

.setting-label {
    font-size: 14px;
    color: var(--text-primary);
    margin-bottom: 10px;
    display: flex;
    justify-content: space-between;
    align-items: center;
}

.setting-value {
    font-size: 13px;
    color: var(--text-secondary);
    font-weight: normal;
}

/* ════════════════════════════════
   Toggle buttons
   ════════════════════════════════ */
.button-group {
    display: flex;
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
    color: var(--text-primary);
    font-size: 13px;
    cursor: pointer;
    transition: all 0.15s;
    -webkit-tap-highlight-color: transparent;
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

/* ════════════════════════════════
   Custom select
   ════════════════════════════════ */
.custom-select {
    position: relative;
    width: 100%;
    box-sizing: border-box;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 8px;
    direction: rtl;
    height: 42px;
    padding: 0 12px;
    user-select: none;
    display: flex;
    align-items: center;
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
    border-radius: 8px;
    max-height: 200px;
    overflow-y: auto;
    z-index: 1001;
    box-shadow: 0 4px 20px rgba(0, 0, 0, 0.15);
}

.select-option {
    padding: 10px 12px;
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

/* ════════════════════════════════
   Slider
   ════════════════════════════════ */
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

/* ════════════════════════════════
   Color palette
   ════════════════════════════════ */
.color-palette {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
    margin-bottom: 10px;
}

.color-swatch {
    width: 36px;
    height: 36px;
    border: 2px solid var(--border-color);
    border-radius: 8px;
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
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
    color: var(--text-secondary);
    text-align: center;
    line-height: 1.1;
    font-weight: bold;
}

.custom-color-row {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-top: 6px;
}

.custom-color-label {
    font-size: 12px;
    color: var(--text-secondary);
    flex: 1;
}

.color-picker {
    width: 36px;
    height: 36px;
    border: 2px solid var(--border-color);
    border-radius: 8px;
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
    font-size: 11px;
    display: flex;
    align-items: center;
    justify-content: center;
}

.clear-color-btn:hover {
    background: var(--hover-bg);
    border-color: var(--accent-color);
}

/* ════════════════════════════════
   Database path
   ════════════════════════════════ */
.database-path-row {
    display: flex;
    gap: 8px;
    align-items: center;
}

.database-path-input {
    flex: 1;
    padding: 10px 12px;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 8px;
    color: var(--text-primary);
    font-size: 13px;
    direction: ltr;
    text-align: left;
    cursor: pointer;
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
    display: flex;
    align-items: center;
    justify-content: center;
    transition: all 0.15s;
}

.database-browse-btn:hover {
    background: var(--accent-hover);
    transform: scale(1.05);
}
</style>