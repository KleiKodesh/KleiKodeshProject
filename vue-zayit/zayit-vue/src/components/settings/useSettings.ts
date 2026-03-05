import { computed } from 'vue';
import { useSettingsStore } from '@/data/stores/settingsStore';

export function useSettings() {
    const settingsStore = useSettingsStore();

    return {
        // Font state
        headerFont: computed(() => settingsStore.headerFont),
        textFont: computed(() => settingsStore.textFont),
        fontSize: computed(() => settingsStore.fontSize),
        linePadding: computed(() => settingsStore.linePadding),

        // Display state
        censorDivineNames: computed(() => settingsStore.censorDivineNames),
        readingBackground: computed(() => settingsStore.readingBackground),

        // Diacritics state
        globalDiacritics: computed(() => settingsStore.globalDiacritics),
        globalDiacriticsState: computed(() => settingsStore.globalDiacriticsState),

        // Theme state
        themePreset: computed({
            get: () => settingsStore.themePreset,
            set: (value) => { settingsStore.themePreset = value; }
        }),

        // PDF state
        pdfPageFilters: computed({
            get: () => settingsStore.pdfPageFilters,
            set: (value) => { settingsStore.pdfPageFilters = value; }
        }),

        // Database state
        databasePath: computed(() => settingsStore.databasePath),

        // UI state
        defaultBookViewToolbarPosition: computed(() => settingsStore.defaultBookViewToolbarPosition),
        newTabPage: computed(() => settingsStore.newTabPage),
        lastSettingsTab: computed(() => settingsStore.lastSettingsTab),

        // Actions
        reset: () => settingsStore.reset(),
    };
}
