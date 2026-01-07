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
    enableVirtualization: boolean
    useOfflineHomepage: boolean
}

const DEFAULT_SETTINGS: Settings = {
    headerFont: "'Segoe UI Variable', 'Segoe UI', system-ui, sans-serif",
    textFont: "'Times New Roman', Times, serif",
    fontSize: 105,
    linePadding: 1.6,
    censorDivineNames: false,
    appZoom: 0.95,
    enableVirtualization: false,
    useOfflineHomepage: true
}

export const useSettingsStore = defineStore('settings', () => {
    const headerFont = ref(DEFAULT_SETTINGS.headerFont)
    const textFont = ref(DEFAULT_SETTINGS.textFont)
    const fontSize = ref(DEFAULT_SETTINGS.fontSize)
    const linePadding = ref(DEFAULT_SETTINGS.linePadding)
    const censorDivineNames = ref(DEFAULT_SETTINGS.censorDivineNames)
    const appZoom = ref(DEFAULT_SETTINGS.appZoom)
    const enableVirtualization = ref(DEFAULT_SETTINGS.enableVirtualization)
    const useOfflineHomepage = ref(DEFAULT_SETTINGS.useOfflineHomepage)

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
                enableVirtualization.value = settings.enableVirtualization ?? DEFAULT_SETTINGS.enableVirtualization
                useOfflineHomepage.value = settings.useOfflineHomepage ?? DEFAULT_SETTINGS.useOfflineHomepage
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
                enableVirtualization: enableVirtualization.value,
                useOfflineHomepage: useOfflineHomepage.value
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
        enableVirtualization.value = DEFAULT_SETTINGS.enableVirtualization
        useOfflineHomepage.value = DEFAULT_SETTINGS.useOfflineHomepage
        localStorage.removeItem(STORAGE_KEY)
        applyCSSVariables()
    }

    // Load settings on init
    loadFromStorage()
    applyCSSVariables()

    // Watch for changes and persist
    watch([headerFont, textFont, fontSize, linePadding, censorDivineNames, appZoom, enableVirtualization, useOfflineHomepage], () => {
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
        enableVirtualization,
        useOfflineHomepage,
        reset
    }
})
