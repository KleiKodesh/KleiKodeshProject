import { computed } from 'vue';
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore';
import { useTabStore } from '@/data/stores/tabStore';

export function useZayitFs() {
    const categoryTreeStore = useCategoryTreeStore();
    const tabStore = useTabStore();

    return {
        // State
        categoryTree: computed(() => categoryTreeStore.categoryTree),
        allBooks: computed(() => categoryTreeStore.allBooks),

        // Actions
        openBook: (bookTitle: string, bookId: number, hasConnections?: boolean) =>
            tabStore.openBook(bookTitle, bookId, hasConnections),
        openBookInNewTab: (bookTitle: string, bookId: number, hasConnections?: boolean) =>
            tabStore.openBookInNewTab(bookTitle, bookId, hasConnections),
    };
}
