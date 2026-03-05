import { onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/data/stores/settingsStore'
import { webviewBridge } from '@/data/services/webviewBridge'

export function useGeneralSettingsTab() {
    const settingsStore = useSettingsStore()
    const {
        censorDivineNames,
        appZoom,
        databasePath,
        globalDiacritics,
        newTabPage,
        defaultBookViewToolbarPosition,
        themePreset
    } = storeToRefs(settingsStore)

    const setCensorDivineNames = (censor: boolean) => {
        censorDivineNames.value = censor
        window.location.reload()
    }

    onMounted(() => {
        if (webviewBridge.isAvailable()) {
            webviewBridge
                .getCurrentDatabasePath()
                .then((p) => {
                    if (p && !databasePath.value) databasePath.value = p
                })
                .catch(() => { })
        }
    })

    return {
        censorDivineNames,
        appZoom,
        databasePath,
        globalDiacritics,
        newTabPage,
        defaultBookViewToolbarPosition,
        themePreset,
        setCensorDivineNames,
        webviewBridge
    }
}
