import { ref, computed, nextTick } from 'vue';
import { onClickOutside } from '@vueuse/core';
import { calculateOnScreenPosition } from './useKeepOnScreen';

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

        // Initially position at click location
        x.value = event.clientX;
        y.value = event.clientY;
        isVisible.value = true;

        // Wait for DOM to render so we can get menu dimensions
        await nextTick();

        // Adjust position to keep menu on screen
        if (menuRef.value) {
            const rect = menuRef.value.getBoundingClientRect();
            const adjustedPosition = calculateOnScreenPosition(
                { x: event.clientX, y: event.clientY },
                rect.width,
                rect.height,
                { horizontalAlign: 'right', verticalAlign: 'bottom' }
            );
            x.value = adjustedPosition.x;
            y.value = adjustedPosition.y;
        }
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
