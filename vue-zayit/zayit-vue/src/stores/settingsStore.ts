import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { applyTheme, getTheme, type ThemePreset } from '../config/themes'
import type { ReadingBackgroundPreset } from '../config/readingBackgrounds'

const STORAGE_KEY = 'zayit-settings'

export type NewTabPage = 'homepage' | 'openfile' | 'hebrewbooks' | 'kezayit-search'
export type BookViewToolbarPosition = 'top' | 'bottom' | 'left' | 'right' | 'float-vertical' | 'float-horizontal'
export type CommentaryToolbarPosition = 'top' | 'bottom'
export type SettingsTab = 'reading' | 'general'

export { type ThemePreset } from '../config/themes'

export interface Settings {
    headerFont: string
    textFont: string
    fontSize: number
    linePadding: number
    censorDivineNames: boolean
    appZoom: number
    readingBackground: ReadingBackgroundPreset
    databasePath: string
    globalDiacritics: boolean
    globalDiacriticsState: number
    newTabPage: NewTabPage
    defaultBookViewToolbarPosition: BookViewToolbarPosition
    commentaryToolbarPosition: CommentaryToolbarPosition
    themePreset: ThemePreset
    lastSettingsTab: SettingsTab
}

const DEFAULT_SETTINGS: Settings = {
    headerFont: "'Segoe UI Variable', 'Segoe UI', system-ui, sans-serif",
    textFont: "'Times New Roman', Times, serif",
    fontSize: 105,
    linePadding: 1.6,
    censorDivineNames: false,
    appZoom: 0.9,
    readingBackground: 'default',
    databasePath: '',
    globalDiacritics: false,
    globalDiacriticsState: 0,
    newTabPage: 'homepage',
    defaultBookViewToolbarPosition: 'top',
    commentaryToolbarPosition: 'top',
    themePreset: 'fluent-light',
    lastSettingsTab: 'general'
}

export const useSettingsStore = defineStore('settings', () => {
    const headerFont = ref(DEFAULT_SETTINGS.headerFont)
    const textFont = ref(DEFAULT_SETTINGS.textFont)
    const fontSize = ref(DEFAULT_SETTINGS.fontSize)
    const linePadding = ref(DEFAULT_SETTINGS.linePadding)
    const censorDivineNames = ref(DEFAULT_SETTINGS.censorDivineNames)
    const appZoom = ref(DEFAULT_SETTINGS.appZoom)
    const readingBackground = ref<ReadingBackgroundPreset>(DEFAULT_SETTINGS.readingBackground)
    const databasePath = ref(DEFAULT_SETTINGS.databasePath)
    const globalDiacritics = ref(DEFAULT_SETTINGS.globalDiacritics)
    const globalDiacriticsState = ref(DEFAULT_SETTINGS.globalDiacriticsState)
    const newTabPage = ref<NewTabPage>(DEFAULT_SETTINGS.newTabPage)
    const defaultBookViewToolbarPosition = ref<BookViewToolbarPosition>(DEFAULT_SETTINGS.defaultBookViewToolbarPosition)
    const commentaryToolbarPosition = ref<CommentaryToolbarPosition>(DEFAULT_SETTINGS.commentaryToolbarPosition)
    const themePreset = ref<ThemePreset>(DEFAULT_SETTINGS.themePreset)
    const lastSettingsTab = ref<SettingsTab>(DEFAULT_SETTINGS.lastSettingsTab)

    const loadFromStorage = () => {
        try {
            const stored = localStorage.getItem(STORAGE_KEY)
            if (stored) {
                const settings = JSON.parse(stored)
                headerFont.value = settings.headerFont || DEFAULT_SETTINGS.headerFont
                textFont.value = settings.textFont || DEFAULT_SETTINGS.textFont
                fontSize.value = settings.fontSize || DEFAULT_SETTINGS.fontSize
                linePadding.value = settings.linePadding || DEFAULT_SETTINGS.linePadding
                censorDivineNames.value = settings.censorDivineNames || DEFAULT_SETTINGS.censorDivineNames
                appZoom.value = settings.appZoom || DEFAULT_SETTINGS.appZoom

                // Migrate old readingBackgroundColor to readingBackground preset
                if (settings.readingBackground) {
                    readingBackground.value = settings.readingBackground as ReadingBackgroundPreset
                } else if (settings.readingBackgroundColor) {
                    // Map old colors to new theme-based presets
                    const colorMap: Record<string, ReadingBackgroundPreset> = {
                        '#FDF6E3': 'cream-light',
                        '#F5F5DC': 'beige-light',
                        '#FAF0E6': 'paper-light',
                        '#F0F8F0': 'green-light',
                        '#F0F8FF': 'blue-light',
                        '#F8F8F8': 'gray-light',
                        '#FFF0F5': 'pink-light',
                        '#FFFACD': 'yellow-light'
                    }
                    readingBackground.value = colorMap[settings.readingBackgroundColor] || 'default'
                } else {
                    readingBackground.value = DEFAULT_SETTINGS.readingBackground
                }

                databasePath.value = settings.databasePath || DEFAULT_SETTINGS.databasePath
                globalDiacritics.value = settings.globalDiacritics ?? DEFAULT_SETTINGS.globalDiacritics
                globalDiacriticsState.value = settings.globalDiacriticsState ?? DEFAULT_SETTINGS.globalDiacriticsState
                newTabPage.value = settings.newTabPage || DEFAULT_SETTINGS.newTabPage
                defaultBookViewToolbarPosition.value = settings.defaultBookViewToolbarPosition || DEFAULT_SETTINGS.defaultBookViewToolbarPosition
                commentaryToolbarPosition.value = settings.commentaryToolbarPosition || DEFAULT_SETTINGS.commentaryToolbarPosition
                lastSettingsTab.value = settings.lastSettingsTab || DEFAULT_SETTINGS.lastSettingsTab

                // Migrate old theme presets to new format
                let themeValue = settings.themePreset || DEFAULT_SETTINGS.themePreset

                // Check if theme exists, if not migrate or use default
                if (!getTheme(themeValue as ThemePreset)) {
                    // Try to migrate old theme names
                    const oldToNewMap: Record<string, ThemePreset> = {
                        'default-light': 'fluent-light',
                        'default-dark': 'fluent-dark',
                        'white-light': 'fluent-light',
                        'white-dark': 'fluent-dark',
                        'paper-light': 'warm-light',
                        'paper-dark': 'warm-dark',
                        'solarized-light': 'sepia-light',
                        'solarized-dark': 'sepia-dark',
                        'gruvbox-light': 'sepia-light',
                        'gruvbox-dark': 'sepia-dark',
                        'github-light': 'fluent-light',
                        'github-dark': 'fluent-dark',
                        'material-light': 'gray-light',
                        'material-dark': 'gray-dark',
                        'one-light': 'fluent-light',
                        'one-dark': 'fluent-dark',
                        'monokai-light': 'night-light',
                        'monokai-dark': 'night-dark',
                        'ayu-light': 'warm-light',
                        'ayu-dark': 'warm-dark',
                        'tokyo-night': 'tokyo-dark',
                        'catppuccin-latte': 'catppuccin-light',
                        'catppuccin-mocha': 'catppuccin-dark'
                    }

                    themeValue = oldToNewMap[themeValue] || 'fluent-light'
                }

                themePreset.value = themeValue as ThemePreset
            }
        } catch (e) {
            console.error('Failed to load settings:', e)
        }
    }

    const saveToStorage = () => {
        try {
            localStorage.setItem(STORAGE_KEY, JSON.stringify({
                headerFont: headerFont.value,
                textFont: textFont.value,
                fontSize: fontSize.value,
                linePadding: linePadding.value,
                censorDivineNames: censorDivineNames.value,
                appZoom: appZoom.value,
                readingBackground: readingBackground.value,
                databasePath: databasePath.value,
                globalDiacritics: globalDiacritics.value,
                globalDiacriticsState: globalDiacriticsState.value,
                newTabPage: newTabPage.value,
                defaultBookViewToolbarPosition: defaultBookViewToolbarPosition.value,
                commentaryToolbarPosition: commentaryToolbarPosition.value,
                themePreset: themePreset.value,
                lastSettingsTab: lastSettingsTab.value
            }))
        } catch (e) {
            console.error('Failed to save settings:', e)
        }
    }

    const applyCSSVariables = () => {
        document.documentElement.style.setProperty('--header-font', headerFont.value)
        document.documentElement.style.setProperty('--text-font', textFont.value)
        document.documentElement.style.setProperty('--font-size', `${fontSize.value}%`)
        document.documentElement.style.setProperty('--line-height', linePadding.value.toString())

        // Apply reading background if not default
        // Reading backgrounds are just theme presets - apply their reading colors
        if (readingBackground.value !== 'default') {
            const bgTheme = getTheme(readingBackground.value as ThemePreset)
            if (bgTheme) {
                // Override reading colors with the selected background's theme reading colors
                document.documentElement.style.setProperty('--reading-bg-primary', bgTheme.reading.bgPrimary)
                document.documentElement.style.setProperty('--reading-bg-secondary', bgTheme.reading.bgSecondary)
                document.documentElement.style.setProperty('--reading-text-primary', bgTheme.reading.textPrimary)
                document.documentElement.style.setProperty('--reading-text-secondary', bgTheme.reading.textSecondary)
                document.documentElement.style.setProperty('--reading-border-color', bgTheme.reading.borderColor)
            }
        }
        // If default, reading colors come from theme (already set by applyTheme)

        // Apply theme preset
        applyTheme(themePreset.value)

        // Apply zoom to the app element
        const appElement = document.getElementById('app')
        if (appElement) {
            appElement.style.zoom = appZoom.value.toString()
        }
    }

    const reset = () => {
        headerFont.value = DEFAULT_SETTINGS.headerFont
        textFont.value = DEFAULT_SETTINGS.textFont
        fontSize.value = DEFAULT_SETTINGS.fontSize
        linePadding.value = DEFAULT_SETTINGS.linePadding
        censorDivineNames.value = DEFAULT_SETTINGS.censorDivineNames
        appZoom.value = DEFAULT_SETTINGS.appZoom
        readingBackground.value = DEFAULT_SETTINGS.readingBackground
        databasePath.value = DEFAULT_SETTINGS.databasePath
        globalDiacritics.value = DEFAULT_SETTINGS.globalDiacritics
        globalDiacriticsState.value = DEFAULT_SETTINGS.globalDiacriticsState
        newTabPage.value = DEFAULT_SETTINGS.newTabPage
        defaultBookViewToolbarPosition.value = DEFAULT_SETTINGS.defaultBookViewToolbarPosition
        commentaryToolbarPosition.value = DEFAULT_SETTINGS.commentaryToolbarPosition
        themePreset.value = DEFAULT_SETTINGS.themePreset
        lastSettingsTab.value = DEFAULT_SETTINGS.lastSettingsTab
        localStorage.removeItem(STORAGE_KEY)
        applyCSSVariables()
    }

    // Load settings on init
    loadFromStorage()
    applyCSSVariables()

    // Watch for changes and persist
    watch([headerFont, textFont, fontSize, linePadding, censorDivineNames, appZoom, readingBackground, databasePath, globalDiacritics, globalDiacriticsState, newTabPage, defaultBookViewToolbarPosition, commentaryToolbarPosition, themePreset, lastSettingsTab], () => {
        saveToStorage()
        applyCSSVariables()
    })

    return {
        headerFont,
        textFont,
        fontSize,
        linePadding,
        censorDivineNames,
        appZoom,
        readingBackground,
        databasePath,
        globalDiacritics,
        globalDiacriticsState,
        newTabPage,
        defaultBookViewToolbarPosition,
        commentaryToolbarPosition,
        themePreset,
        lastSettingsTab,
        reset
    }
})
