import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

const STORAGE_KEY = 'zayit-settings'

export interface Settings {
    headerFont: string
    textFont: string
    fontSize: number
    linePadding: number
    censorDivineNames: boolean
    appZoom: number
    readingBackgroundColor: string
    databasePath: string
}

const DEFAULT_SETTINGS: Settings = {
    headerFont: "'Segoe UI Variable', 'Segoe UI', system-ui, sans-serif",
    textFont: "'Times New Roman', Times, serif",
    fontSize: 105,
    linePadding: 1.6,
    censorDivineNames: false,
    appZoom: 0.95,
    readingBackgroundColor: '',
    databasePath: ''
}

export const useSettingsStore = defineStore('settings', () => {
    const headerFont = ref(DEFAULT_SETTINGS.headerFont)
    const textFont = ref(DEFAULT_SETTINGS.textFont)
    const fontSize = ref(DEFAULT_SETTINGS.fontSize)
    const linePadding = ref(DEFAULT_SETTINGS.linePadding)
    const censorDivineNames = ref(DEFAULT_SETTINGS.censorDivineNames)
    const appZoom = ref(DEFAULT_SETTINGS.appZoom)
    const readingBackgroundColor = ref(DEFAULT_SETTINGS.readingBackgroundColor)
    const databasePath = ref(DEFAULT_SETTINGS.databasePath)

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
                readingBackgroundColor.value = settings.readingBackgroundColor || DEFAULT_SETTINGS.readingBackgroundColor
                databasePath.value = settings.databasePath || DEFAULT_SETTINGS.databasePath
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
                readingBackgroundColor: readingBackgroundColor.value,
                databasePath: databasePath.value
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
        document.documentElement.style.setProperty('--reading-bg-color', readingBackgroundColor.value)

        // Auto-adjust text color based on reading background brightness
        if (readingBackgroundColor.value) {
            const textColor = getContrastingTextColor(readingBackgroundColor.value)
            document.documentElement.style.setProperty('--reading-text-color', textColor)
        } else {
            // Use default theme text color when no custom background
            document.documentElement.style.setProperty('--reading-text-color', 'var(--text-primary)')
        }

        // Apply zoom to the app element
        const appElement = document.getElementById('app')
        if (appElement) {
            appElement.style.zoom = appZoom.value.toString()
        }
    }

    // Helper function to determine contrasting text color based on background brightness
    const getContrastingTextColor = (backgroundColor: string): string => {
        // Convert hex to RGB
        const hex = backgroundColor.replace('#', '')
        const r = parseInt(hex.substr(0, 2), 16)
        const g = parseInt(hex.substr(2, 2), 16)
        const b = parseInt(hex.substr(4, 2), 16)

        // Calculate relative luminance using WCAG formula
        const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255

        // Return dark text for light backgrounds, light text for dark backgrounds
        return luminance > 0.5 ? '#1f1f1f' : '#ffffff'
    }

    const reset = () => {
        headerFont.value = DEFAULT_SETTINGS.headerFont
        textFont.value = DEFAULT_SETTINGS.textFont
        fontSize.value = DEFAULT_SETTINGS.fontSize
        linePadding.value = DEFAULT_SETTINGS.linePadding
        censorDivineNames.value = DEFAULT_SETTINGS.censorDivineNames
        appZoom.value = DEFAULT_SETTINGS.appZoom
        readingBackgroundColor.value = DEFAULT_SETTINGS.readingBackgroundColor
        databasePath.value = DEFAULT_SETTINGS.databasePath
        localStorage.removeItem(STORAGE_KEY)
        applyCSSVariables()
    }

    // Load settings on init
    loadFromStorage()
    applyCSSVariables()

    // Watch for changes and persist
    watch([headerFont, textFont, fontSize, linePadding, censorDivineNames, appZoom, readingBackgroundColor, databasePath], () => {
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
        readingBackgroundColor,
        databasePath,
        reset
    }
})
