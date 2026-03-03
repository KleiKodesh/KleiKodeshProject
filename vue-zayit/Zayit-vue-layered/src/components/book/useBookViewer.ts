import { computed } from 'vue';
import { useTabStore } from '@/data/stores/tabStore';

export function useBookViewer(tabId?: number) {
    const tabStore = useTabStore();
    const tab = tabId ? computed(() => tabStore.tabs.find(t => t.id === tabId)) : computed(() => tabStore.activeTab);

    return {
        // State
        tab,
        tabs: computed(() => tabStore.tabs),
        activeTab: computed(() => tabStore.activeTab),
        currentDiacriticsState: computed(() => tabStore.currentDiacriticsState),

        // Book actions
        openBook: (bookTitle: string, bookId: number, hasConnections?: boolean, initialLineIndex?: number) =>
            tabStore.openBook(bookTitle, bookId, hasConnections, initialLineIndex),
        openBookInNewTab: (bookTitle: string, bookId: number, hasConnections?: boolean, initialLineIndex?: number) =>
            tabStore.openBookInNewTab(bookTitle, bookId, hasConnections, initialLineIndex),
        openBookToc: (bookTitle: string, bookId: number, hasConnections?: boolean) =>
            tabStore.openBookToc(bookTitle, bookId, hasConnections),
        closeToc: () => tabStore.closeToc(),

        // Book state toggles
        toggleSplitPane: () => tabStore.toggleSplitPane(),
        toggleToolbar: () => tabStore.toggleToolbar(),
        toggleBookSearch: (isOpen: boolean) => tabStore.toggleBookSearch(isOpen),
        toggleAltTocDisplay: () => tabStore.toggleAltTocDisplay(),
        toggleDiacritics: () => tabStore.toggleDiacritics(),
        toggleLineDisplay: () => tabStore.toggleLineDisplay(),

        // Zoom
        zoomIn: () => tabStore.zoomIn(),
        zoomOut: () => tabStore.zoomOut(),
        resetZoom: () => tabStore.resetZoom(),
    };
}
