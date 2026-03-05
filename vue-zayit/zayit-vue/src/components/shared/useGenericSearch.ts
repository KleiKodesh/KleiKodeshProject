import { ref, watch, nextTick, computed } from 'vue';

export function useGenericSearch(
    props: {
        isOpen: boolean;
        currentMatchIndex?: number;
        totalMatches?: number;
    },
    emit: any
) {
    const searchQuery = ref('');
    const searchInputRef = ref<HTMLInputElement | null>(null);
    let debounceTimeout: number | null = null;

    const displayCount = computed(() => {
        if (props.totalMatches === 0) return '0/0';
        return `${(props.currentMatchIndex ?? -1) + 1}/${props.totalMatches}`;
    });

    watch(() => props.isOpen, async (isOpen) => {
        if (isOpen) {
            await nextTick();
            searchInputRef.value?.focus();
            searchInputRef.value?.select();
        } else {
            searchQuery.value = '';
        }
    });

    watch(searchQuery, (query) => {
        if (debounceTimeout !== null) {
            clearTimeout(debounceTimeout);
        }
        debounceTimeout = window.setTimeout(() => {
            emit('search', query);
        }, 300);
    });

    function findNext() {
        emit('next');
    }

    function findPrevious() {
        emit('previous');
    }

    function close() {
        emit('close');
    }

    return {
        searchQuery,
        searchInputRef,
        displayCount,
        findNext,
        findPrevious,
        close
    };
}
