import { computed } from 'vue';
import { useHebrewBooksStore } from '@/data/stores/hebrewBooksStore';
import { useTabStore } from '@/data/stores/tabStore';

export function useHebrewBooks() {
    const store = useHebrewBooksStore();
    const tabStore = useTabStore();

    return {
        // State
        filteredBooks: computed(() => store.filteredBooks),
        isLoading: computed(() => store.isLoading),
        error: computed(() => store.error),
        searchTerm: computed(() => store.searchTerm),
        currentView: computed(() => store.currentView),
        selectedBookId: computed(() => store.selectedBookId),
        hasBooks: computed(() => store.hasBooks),
        selectedBook: computed(() => store.selectedBook),
        activeTab: computed(() => tabStore.activeTab),

        // Actions
        loadBooks: () => store.loadBooks(),
        performSearch: (term: string) => store.performSearch(term),
        trackBookInteraction: (bookId: string) => store.trackBookInteraction(bookId),
        openBookViewer: (bookId: string) => store.openBookViewer(bookId),
        closeBookViewer: () => store.closeBookViewer(),
        openHebrewBookViewer: (bookId: string, title: string) => store.openHebrewBookViewer(bookId, title),
        downloadHebrewBook: (bookId: string, title: string) => store.downloadHebrewBook(bookId, title),
    };
}
