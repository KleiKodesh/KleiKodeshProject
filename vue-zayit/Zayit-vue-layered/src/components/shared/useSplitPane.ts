import { ref, computed } from 'vue';

export function useSplitPane(
    props: {
        initialTopHeight?: number;
    },
    emit: (event: 'resize', topHeight: number, bottomHeight: number) => void
) {
    const topHeight = ref(props.initialTopHeight || 40);
    const bottomHeight = computed(() => 100 - topHeight.value);
    const isResizing = ref(false);

    const startResize = (event: MouseEvent | TouchEvent) => {
        isResizing.value = true;
        event.preventDefault();

        const container = (event.target as HTMLElement).closest('.split-pane-container') as HTMLElement;
        if (!container) return;

        const containerRect = container.getBoundingClientRect();

        const getClientY = (e: MouseEvent | TouchEvent): number => {
            if (e instanceof MouseEvent) {
                return e.clientY;
            } else {
                return e.touches[0]?.clientY ?? 0;
            }
        };

        const handleMove = (e: MouseEvent | TouchEvent) => {
            if (!isResizing.value) return;

            const clientY = getClientY(e);
            const relativeY = clientY - containerRect.top;
            const newTopHeight = (relativeY / containerRect.height) * 100;

            // Constrain between 20% and 80%
            if (newTopHeight >= 20 && newTopHeight <= 80) {
                topHeight.value = newTopHeight;
                emit('resize', topHeight.value, bottomHeight.value);
            }
        };

        const handleEnd = () => {
            isResizing.value = false;
            document.removeEventListener('mousemove', handleMove);
            document.removeEventListener('mouseup', handleEnd);
            document.removeEventListener('touchmove', handleMove);
            document.removeEventListener('touchend', handleEnd);
        };

        document.addEventListener('mousemove', handleMove);
        document.addEventListener('mouseup', handleEnd);
        document.addEventListener('touchmove', handleMove);
        document.addEventListener('touchend', handleEnd);
    };

    return {
        topHeight,
        bottomHeight,
        startResize
    };
}
