<template>
    <div class="flex-column width-fill height-fill settings-page">
        <div class="flex-110 overflow-y settings-content">
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

            <!-- Reset Button -->
            <div class="setting-group">
                <button @click="resetSettings"
                        class="width-fill c-pointer bold reset-button">
                    איפוס הגדרות
                </button>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '../../stores/settingsStore'
import { hebrewFonts } from '../../data/hebrewFonts'
import { useConnectivity } from '../../utils/connectivity'

const settingsStore = useSettingsStore()
const { headerFont, textFont, fontSize, linePadding, censorDivineNames, appZoom, useOfflineHomepage } = storeToRefs(settingsStore)
const { isOnline } = useConnectivity()

const availableFonts = ref<string[]>([])
const isHeaderDropdownOpen = ref(false)
const isTextDropdownOpen = ref(false)

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

const resetSettings = () => {
    settingsStore.reset()
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

onMounted(() => {
    detectFonts()
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
</style>
