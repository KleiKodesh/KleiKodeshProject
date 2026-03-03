import { ref, computed, onMounted, onUnmounted } from 'vue';
import { useTabs } from '@/components/workspace/useTabs';

export function useTabListDropdown() {
    const { tabs, setActiveTab, closeTabById } = useTabs();
    const isVisible = ref(false);

    // Filter out settings and workspaces pages from dropdown
    const visibleTabs = computed(() => {
        return tabs.value.filter(tab =>
            tab.currentPage !== 'settings' && tab.currentPage !== 'workspaces'
        );
    });

    const toggle = () => {
        isVisible.value = !isVisible.value;
    };

    const close = () => {
        isVisible.value = false;
    };

    const selectTab = (id: number) => {
        setActiveTab(id);
        close();
    };

    const handleWindowBlur = () => {
        if (isVisible.value) {
            close();
        }
    };

    const handleVisibilityChange = () => {
        if (document.hidden && isVisible.value) {
            close();
        }
    };

    onMounted(() => {
        window.addEventListener('blur', handleWindowBlur);
        document.addEventListener('visibilitychange', handleVisibilityChange);
    });

    onUnmounted(() => {
        window.removeEventListener('blur', handleWindowBlur);
        document.removeEventListener('visibilitychange', handleVisibilityChange);
    });

    return {
        isVisible,
        visibleTabs,
        toggle,
        close,
        selectTab,
        closeTabById
    };
}
