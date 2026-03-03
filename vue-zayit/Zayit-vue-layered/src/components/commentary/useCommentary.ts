import { computed } from 'vue';
import { useTabStore } from '@/data/stores/tabStore';
import { useConnectionTypesStore } from '@/data/stores/connectionTypesStore';

export function useCommentary(tabId?: number) {
    const tabStore = useTabStore();
    const connectionTypesStore = useConnectionTypesStore();

    const tab = tabId ? computed(() => tabStore.tabs.find(t => t.id === tabId)) : computed(() => tabStore.activeTab);

    return {
        // State
        tab,
        connectionTypes: computed(() => connectionTypesStore.connectionTypes),

        // Actions
        toggleSplitPane: () => tabStore.toggleSplitPane(),
    };
}
