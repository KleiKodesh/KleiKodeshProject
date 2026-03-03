import { ref, computed, nextTick } from 'vue';
import { onClickOutside } from '@vueuse/core';

export interface ContextMenuItem {
    label: string;
    action: () => void;
}

export function useContextMenu() {
    const isVisible = ref(false);
    const x = ref(0);
    const y = ref(0);
    const menuRef = ref<HTMLElement>();

    const menuStyle = computed(() => ({
        left: `${x.value}px`,
        top: `${y.value}px`
    }));

    async function show(event: MouseEvent) {
        event.preventDefault();
        event.stopPropagation();
        x.value = event.clientX;
        y.value = event.clientY;
        isVisible.value = true;

        // Wait for next tick to set up click outside listener
        await nextTick();
    }

    function hide() {
        isVisible.value = false;
    }

    function handleItemClick(item: ContextMenuItem) {
        item.action();
        hide();
    }

    // Setup click outside handler
    const setupClickOutside = () => {
        onClickOutside(menuRef, () => {
            if (isVisible.value) {
                hide();
            }
        });
    };

    return {
        isVisible,
        menuRef,
        menuStyle,
        show,
        hide,
        handleItemClick,
        setupClickOutside
    };
}
