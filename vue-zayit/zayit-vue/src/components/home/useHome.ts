import { computed } from 'vue';
import { useTabStore } from '@/data/stores/tabStore';
import { useWorkspaceStore } from '@/data/stores/workspaceStore';

export function useHome() {
    const tabStore = useTabStore();
    const workspaceStore = useWorkspaceStore();

    return {
        // Workspace state
        workspaces: computed(() => workspaceStore.workspaces),
        currentWorkspace: computed(() => workspaceStore.currentWorkspace),

        // Workspace actions
        createWorkspace: (name: string) => workspaceStore.createWorkspace(name),
        switchWorkspace: (id: string) => workspaceStore.switchWorkspace(id),
        deleteWorkspace: (id: string) => workspaceStore.deleteWorkspace(id),

        // Tab opening actions (from tabStore)
        openZayitOpenFilePage: () => tabStore.openZayitOpenFilePage(),
        openSettings: () => tabStore.openSettings(),
        openHebrewBooks: () => tabStore.openHebrewBooks(),
        openKezayitSearch: () => tabStore.openKezayitSearch(),
        openWorkspaceManager: () => tabStore.openWorkspaceManager(),
        openPdfWithFile: (fileName: string, fileUrl: string) => tabStore.openPdfWithFile(fileName, fileUrl),
        openPdfWithFilePathAndBlobUrl: (fileName: string, filePath: string, blobUrl: string) =>
            tabStore.openPdfWithFilePathAndBlobUrl(fileName, filePath, blobUrl),
    };
}
