import { computed } from 'vue';
import { useTabStore } from '@/data/stores/tabStore';

export function useTabs() {
    const tabStore = useTabStore();

    return {
        // State
        tabs: computed(() => tabStore.tabs),
        activeTab: computed(() => tabStore.activeTab),

        // Actions
        addTab: () => tabStore.addTab(),
        closeTab: () => tabStore.closeTab(),
        closeTabById: (id: number) => tabStore.closeTabById(id),
        closeAllTabs: () => tabStore.closeAllTabs(),
        setActiveTab: (id: number) => tabStore.setActiveTab(id),
        resetTab: () => tabStore.resetTab(),
        closeToc: () => tabStore.closeToc(),
    };
}
