import { ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/data/stores/settingsStore'
import { useTabStore } from '@/data/stores/tabStore'
import { webviewBridge } from '@/data/services/webviewBridge'
import { addCustomTheme, deleteCustomTheme, getTheme, type ThemePreset, type ThemeColors } from '@/utils/themes'
import type { SettingsTab } from '@/data/stores/settingsStore'
import type { DialogOptions } from '@/components/shared/useDialog'

export function useSettingsPage(
    confirm: (message: string, options?: Omit<DialogOptions, 'message'>) => Promise<boolean>,
    error: (message: string, options?: Omit<DialogOptions, 'message'>) => Promise<boolean>
) {
    const settingsStore = useSettingsStore()
    const tabStore = useTabStore()
    const { databasePath, themePreset, lastSettingsTab } = storeToRefs(settingsStore)

    const activeTab = ref<SettingsTab>(lastSettingsTab.value)
    const showCustomThemeCreator = ref(false)

    // Watch activeTab and update lastSettingsTab in store
    watch(activeTab, (newTab) => {
        lastSettingsTab.value = newTab
    })

    // Update tab title when theme creator opens/closes
    watch(showCustomThemeCreator, (isOpen) => {
        const tab = tabStore.tabs.find(t => t.isActive && t.currentPage === 'settings')
        if (tab) {
            tab.title = isOpen ? 'הגדרות - צור ערכת נושא' : 'הגדרות'
        }
    })

    // Reset settings
    const resetSettings = async () => {
        const confirmed = await confirm(
            'האם אתה בטוח שברצונך לאפס את כל ההגדרות? פעולה זו תחזיר את האפליקציה למצב ברירת המחדל.',
            { title: 'איפוס הגדרות', confirmVariant: 'danger' }
        )
        if (!confirmed) return

        settingsStore.reset()

        if (webviewBridge.isAvailable()) {
            try {
                await webviewBridge.clearDatabasePath()
            } catch { }
        }

        window.location.reload()
    }

    // Database file selection
    const selectDatabaseFile = async () => {
        try {
            const result = await webviewBridge.openDatabaseFilePicker()
            if (!result.filePath) return

            const isValid = await webviewBridge.validateDatabasePath(result.filePath)
            if (!isValid) {
                await error('הקובץ שנבחר אינו מסד נתונים תקין של SQLite.')
                return
            }

            databasePath.value = result.filePath
            const ok = await webviewBridge.setDatabasePath(result.filePath)

            if (ok) {
                window.location.reload()
            } else {
                await error('שגיאה בהגדרת מיקום מסד הנתונים. אנא נסה שוב.')
                databasePath.value = ''
            }
        } catch {
            await error('שגיאה בבחירת קובץ מסד הנתונים. אנא נסה שוב.')
        }
    }

    // Theme creator functions
    const openCustomThemeCreator = () => {
        showCustomThemeCreator.value = true
    }

    const closeCustomThemeCreator = () => {
        showCustomThemeCreator.value = false
    }

    const handleCustomThemeSave = (
        themes: Array<{
            id: string
            name: string
            isDark: boolean
            reading: ThemeColors
            ui: ThemeColors
        }>
    ) => {
        themes.forEach((themeData) => {
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

    const deleteCustomThemeHandler = async (id: string) => {
        const confirmed = await confirm(
            `האם למחוק את ערכת הנושא "${getTheme(id as ThemePreset)?.name}"?`
        )
        if (confirmed) {
            deleteCustomTheme(id)
            if (themePreset.value === id) {
                themePreset.value = 'fluent-light'
            }
        }
    }

    return {
        activeTab,
        showCustomThemeCreator,
        resetSettings,
        selectDatabaseFile,
        openCustomThemeCreator,
        closeCustomThemeCreator,
        handleCustomThemeSave,
        deleteCustomThemeHandler
    }
}
