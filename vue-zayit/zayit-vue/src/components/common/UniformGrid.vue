<template>
    <div class="uniform-grid"
         :style="gridStyle"
         ref="gridRef">
        <slot />
    </div>
</template>

<script setup lang="ts">
import { computed, ref, onMounted, onUpdated, nextTick, onBeforeUnmount } from 'vue';

interface Props {
    minItemWidth?: number;
    maxItemWidth?: number;
    gap?: string;
    maxWidth?: string;
    forceColumns?: number;
}

const props = withDefaults(defineProps<Props>(), {
    minItemWidth: 100,
    maxItemWidth: 140,
    gap: '1rem',
    maxWidth: 'min(90vw, 500px)'
});

const gridRef = ref<HTMLElement>();
const itemCount = ref(0);
const containerWidth = ref(0);

// Calculate optimal grid dimensions for even distribution
const calculateOptimalGrid = (items: number): { rows: number; cols: number } => {
    if (items <= 0) return { rows: 1, cols: 1 };
    if (items === 1) return { rows: 1, cols: 1 };
    if (items === 2) return { rows: 1, cols: 2 };
    if (items === 3) return { rows: 1, cols: 3 };
    if (items === 4) return { rows: 2, cols: 2 };
    if (items === 5) return { rows: 2, cols: 3 };
    if (items === 6) return { rows: 2, cols: 3 };
    if (items === 7) return { rows: 3, cols: 3 };
    if (items === 8) return { rows: 3, cols: 3 };
    if (items === 9) return { rows: 3, cols: 3 };

    // For larger numbers, find the most square-like arrangement
    const sqrt = Math.sqrt(items);
    const cols = Math.ceil(sqrt);
    const rows = Math.ceil(items / cols);

    return { rows, cols };
};

// Check if all items can fit in one row based on container width
const canFitInOneRow = computed(() => {
    if (itemCount.value === 0 || containerWidth.value === 0) return true;

    // Parse gap value (assume rem, convert to px - approximate)
    const gapPx = parseFloat(props.gap) * 16; // 1rem ≈ 16px
    const totalGapWidth = (itemCount.value - 1) * gapPx;
    const availableWidth = containerWidth.value - totalGapWidth;
    const itemWidth = props.maxItemWidth; // Use maxItemWidth for calculation

    // Add some buffer to prevent edge cases where items barely fit
    const bufferWidth = 40; // Increased buffer for larger tiles
    return availableWidth >= (itemWidth * itemCount.value) + bufferWidth;
});

const optimalGrid = computed(() => {
    if (props.forceColumns) {
        return {
            rows: Math.ceil(itemCount.value / props.forceColumns),
            cols: props.forceColumns
        };
    }

    // If all items can fit in one row, use single row
    if (canFitInOneRow.value) {
        return { rows: 1, cols: itemCount.value };
    }

    // Otherwise, use optimal balanced distribution
    return calculateOptimalGrid(itemCount.value);
});

const gridStyle = computed(() => ({
    '--min-item-width': `${props.minItemWidth}px`,
    '--max-item-width': `${props.maxItemWidth}px`,
    '--grid-gap': props.gap,
    '--grid-max-width': props.maxWidth,
    '--grid-columns': optimalGrid.value.cols,
    '--grid-rows': optimalGrid.value.rows
}));

const updateDimensions = () => {
    if (gridRef.value) {
        itemCount.value = gridRef.value.children.length;
        // Use parent container width for space calculation
        const parentContainer = gridRef.value.parentElement;
        containerWidth.value = parentContainer ? parentContainer.offsetWidth : gridRef.value.offsetWidth;
    }
};

// Debounced update for better performance
let updateTimeout: number | null = null;
const debouncedUpdate = () => {
    if (updateTimeout) clearTimeout(updateTimeout);
    updateTimeout = setTimeout(updateDimensions, 50) as unknown as number;
};

// Resize observer to track container width changes
let resizeObserver: ResizeObserver | null = null;

onMounted(() => {
    nextTick(() => {
        updateDimensions();

        if (gridRef.value && window.ResizeObserver) {
            const parentContainer = gridRef.value.parentElement;
            const observeTarget = parentContainer || gridRef.value;

            resizeObserver = new ResizeObserver(() => {
                debouncedUpdate();
            });
            resizeObserver.observe(observeTarget);
        }
    });
});

onUpdated(() => {
    nextTick(() => {
        updateDimensions();
    });
});

// Cleanup
onBeforeUnmount(() => {
    if (resizeObserver) {
        resizeObserver.disconnect();
        resizeObserver = null;
    }
    if (updateTimeout) {
        clearTimeout(updateTimeout);
        updateTimeout = null;
    }
});
</script>

<style scoped>
.uniform-grid {
    display: grid;
    gap: var(--grid-gap);
    max-width: var(--grid-max-width);
    width: 100%;
    grid-template-columns: repeat(var(--grid-columns), minmax(var(--min-item-width), var(--max-item-width)));
    justify-content: center;
    align-items: center;
    transition: grid-template-columns 0.3s ease;
    margin: 0 auto;
}

/* Grid items styling - consistent sizing */
.uniform-grid :deep(> *) {
    aspect-ratio: 1;
    width: 100%;
    height: 100%;
    transition: all 0.3s ease;
}

/* Responsive fallbacks for very small screens */
@media (max-width: 300px) {
    .uniform-grid {
        grid-template-columns: repeat(min(var(--grid-columns), 2), 1fr);
    }
}

@media (max-width: 200px) {
    .uniform-grid {
        grid-template-columns: 1fr;
    }
}
</style>