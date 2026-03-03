import { computed } from 'vue';
import { useTabStore } from '@/data/stores/tabStore';

export function usePdf(tabId?: number) {
    const tabStore = useTabStore();
    const tab = tabId ? computed(() => tabStore.tabs.find(t => t.id === tabId)) : computed(() => tabStore.activeTab);

    return {
        // State
        tab,
        activeTab: computed(() => tabStore.activeTab),

        // Actions
        openPdf: (fileName: string, fileUrl: string) => tabStore.openPdf(fileName, fileUrl),
        openPdfWithFile: (fileName: string, fileUrl: string) => tabStore.openPdfWithFile(fileName, fileUrl),
        openPdfWithFilePath: (fileName: string, filePath: string) => tabStore.openPdfWithFilePath(fileName, filePath),
        openPdfWithFilePathAndBlobUrl: (fileName: string, filePath: string, blobUrl: string) =>
            tabStore.openPdfWithFilePathAndBlobUrl(fileName, filePath, blobUrl),
    };
}
