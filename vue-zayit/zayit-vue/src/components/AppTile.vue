<template>
    <div class="app-tile"
         @click="$emit('click')">
        <div class="tile-icon">
            <img v-if="imageSrc"
                 :src="imageSrc"
                 :alt="label"
                 class="tile-image" />
            <Icon v-else-if="icon"
                  :icon="icon" />
        </div>
        <span class="tile-label">{{ label }}</span>
    </div>
</template>

<script setup lang="ts">
import { Icon } from '@iconify/vue';

interface Props {
    label: string;
    icon?: string;
    imageSrc?: string;
}

defineProps<Props>();
defineEmits<{
    click: [];
}>();
</script>

<style scoped>
.app-tile {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    background: var(--bg-secondary);
    border-radius: 16px;
    cursor: pointer;
    transition: all 0.2s ease;
    border: 1px solid var(--border-color);
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
    padding: 0.4rem;
    height: 100%;
    container-type: inline-size;
    /* Enable container queries */
}

.app-tile:hover {
    transform: scale(1.05);
    background: var(--hover-bg);
    box-shadow: 0 4px 16px rgba(0, 0, 0, 0.15);
}

.app-tile:active {
    transform: scale(0.98);
}

.tile-icon {
    margin-bottom: 0.25rem;
    flex-shrink: 0;
}

.tile-icon svg {
    width: clamp(1.5rem, 25%, 2.5rem);
    height: clamp(1.5rem, 25%, 2.5rem);
}

.tile-image {
    width: clamp(1.5rem, 25%, 2.5rem);
    height: clamp(1.5rem, 25%, 2.5rem);
    object-fit: contain;
}

.tile-label {
    font-size: clamp(0.6rem, 2.5vw, 1rem);
    font-weight: 500;
    color: var(--text-primary);
    text-align: center;
    line-height: 1.1;
    overflow: hidden;
    display: -webkit-box;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    width: 100%;
    word-break: break-word;
}

/* Dark theme adjustments */
:root.dark .app-tile {
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.3);
}

:root.dark .app-tile:hover {
    box-shadow: 0 4px 16px rgba(0, 0, 0, 0.4);
}

/* Container queries for responsive content sizing */
@container (max-width: 120px) {

    .tile-icon svg,
    .tile-image {
        width: 1.5rem;
        height: 1.5rem;
    }

    .tile-label {
        font-size: 0.65rem;
    }
}

@container (min-width: 121px) and (max-width: 160px) {

    .tile-icon svg,
    .tile-image {
        width: 2rem;
        height: 2rem;
    }

    .tile-label {
        font-size: 0.75rem;
    }
}

@container (min-width: 161px) {

    .tile-icon svg,
    .tile-image {
        width: 2.5rem;
        height: 2.5rem;
    }

    .tile-label {
        font-size: 0.9rem;
    }
}
</style>