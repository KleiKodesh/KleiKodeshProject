<template>
    <div class="commentary-group-header">
        <a v-if="bookId !== undefined && lineIndex !== undefined"
           href="#"
           class="commentary-group-title"
           @click.prevent="emit('click')">
            {{ displayPath }}
        </a>
        <h3 v-else
            class="commentary-group-title">{{ displayPath }}</h3>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
    path: string[]
    bookId?: number
    lineIndex?: number
}>()

const emit = defineEmits<{
    (e: 'click'): void
}>()

const displayPath = computed(() => props.path.join(' > '))
</script>

<style scoped>
.commentary-group-header {
    position: sticky;
    top: 0;
    background-color: var(--reading-bg-primary);
    padding: 8px 0;
    z-index: 10;
}

.commentary-group-title {
    margin: 0;
    font-size: calc(1.1rem * var(--commentary-font-size) / 100);
    font-weight: 600;
    color: var(--reading-text-primary);
    direction: rtl;
    font-family: var(--commentary-header-font);
}

a.commentary-group-title {
    color: var(--accent-color);
    text-decoration: none;
    cursor: pointer;
}

a.commentary-group-title:hover {
    text-decoration: underline;
}
</style>
