<template>
    <div class="uniform-grid"
         :style="gridStyle"
         ref="gridRef">
        <slot />
    </div>
</template>

<script setup lang="ts">
import { useUniformGrid } from '@/components/shared/useUniformGrid';

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

const { gridRef, gridStyle } = useUniformGrid(props);
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