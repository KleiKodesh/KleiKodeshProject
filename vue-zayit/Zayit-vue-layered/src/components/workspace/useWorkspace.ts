import { computed } from 'vue';
import { useWorkspaceStore } from '@/data/stores/workspaceStore';
import { useTabStore } from '@/data/stores/tabStore';

export function useWorkspace() {
    const workspaceStore = useWorkspaceStore();
    const tabStore = useTabStore();

    return {
        // Workspace state
        workspaces: computed(() => tabStore.workspaces),
        currentWorkspace: computed(() => tabStore.currentWorkspaceId),

        // Tab state
        tabs: computed(() => tabStore.tabs),
        activeTab: computed(() => tabStore.activeTab),

        // Workspace actions
        createWorkspace: (name: string) => tabStore.createWorkspace(name),
        switchWorkspace: (id: string) => tabStore.switchWorkspace(id),
        deleteWorkspace: (id: string) => tabStore.deleteWorkspace(id),
        renameWorkspace: (id: string, name: string) => tabStore.renameWorkspace(id, name),
        getWorkspaceName: (id: string) => tabStore.getWorkspaceName(id),

        // Tab actions
        addTab: () => tabStore.addTab(),
        closeTab: () => tabStore.closeTab(),
        closeTabById: (id: number) => tabStore.closeTabById(id),
        setActiveTab: (id: number) => tabStore.setActiveTab(id),
        resetTab: () => tabStore.resetTab(),
    };
}
