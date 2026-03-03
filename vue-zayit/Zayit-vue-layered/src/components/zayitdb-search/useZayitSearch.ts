import { computed } from 'vue';
import { useCategoryTreeStore } from '@/data/stores/categoryTreeStore';

export function useZayitSearch() {
    const categoryTreeStore = useCategoryTreeStore();

    return {
        // State
        categoryTree: computed(() => categoryTreeStore.categoryTree),
        allBooks: computed(() => categoryTreeStore.allBooks),
    };
}
