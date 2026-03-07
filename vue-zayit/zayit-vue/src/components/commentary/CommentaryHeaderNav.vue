<template>
    <div class="commentary-header-nav">
        <button class="commentary-nav-btn"
                :disabled="!hasPrevious"
                :title="hasPrevious ? 'מפרש קודם' : 'אין מפרש קודם'"
                @click="emit('navigate-previous')">
            <Icon icon="fluent:chevron-up-28-regular" />
        </button>
        <button class="commentary-nav-btn"
                :disabled="!hasNext"
                :title="hasNext ? 'מפרש הבא' : 'אין מפרש הבא'"
                @click="emit('navigate-next')">
            <Icon icon="fluent:chevron-down-28-regular" />
        </button>

        <div class="nav-separator"></div>

        <button class="commentary-nav-btn"
                :title="'שורה קודמת'"
                @click="emit('navigate-previous-line')">
            <Icon icon="fluent:chevron-right-28-regular" />
        </button>
        <button class="commentary-nav-btn"
                :title="'שורה הבאה'"
                @click="emit('navigate-next-line')">
            <Icon icon="fluent:chevron-left-28-regular" />
        </button>

        <div class="nav-separator"></div>

        <button v-if="showBookButton"
                class="commentary-nav-btn"
                :title="'עבור לשורה בספר'"
                @click="emit('navigate-to-book')">
            <Icon icon="fluent:book-open-24-regular" />
        </button>
    </div>
</template>

<script setup lang="ts">
import { Icon } from '@iconify/vue'

defineProps<{
    hasPrevious?: boolean
    hasNext?: boolean
    showBookButton?: boolean
}>()

const emit = defineEmits<{
    (e: 'navigate-previous'): void
    (e: 'navigate-next'): void
    (e: 'navigate-previous-line'): void
    (e: 'navigate-next-line'): void
    (e: 'navigate-to-book'): void
}>()
</script>

<style scoped>
.commentary-header-nav {
    display: flex;
    gap: 4px;
    align-items: center;
}

.nav-separator {
    width: 1px;
    height: 20px;
    background-color: var(--border-color);
    margin: 0 4px;
}

.commentary-nav-btn {
    width: calc(1.1rem * var(--commentary-font-size) / 100);
    height: calc(1.1rem * var(--commentary-font-size) / 100);
    display: flex;
    align-items: center;
    justify-content: center;
    background-color: transparent;
    border: none;
    cursor: pointer;
    padding: 0;
    color: var(--text-primary);
    transition: color 0.2s;
}

.commentary-nav-btn:hover:not(:disabled) {
    color: var(--accent-color);
}

.commentary-nav-btn:disabled {
    opacity: 0.4;
    cursor: not-allowed;
}
</style>
