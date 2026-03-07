<template>
    <div ref="toolbarRef" class="commentary-toolbar">
        <h3 class="commentary-toolbar-title ellipsis">{{ displayPath }}</h3>

        <CommentaryHeaderNav v-if="showNav"
                             :has-previous="hasPrevious"
                             :has-next="hasNext"
                             :show-book-button="bookId !== undefined && lineIndex !== undefined"
                             @navigate-previous="emit('navigate-previous')"
                             @navigate-next="emit('navigate-next')"
                             @navigate-previous-line="emit('navigate-previous-line')"
                             @navigate-next-line="emit('navigate-next-line')"
                             @navigate-to-book="emit('click')" />
    </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useElementHover } from '@vueuse/core'
import CommentaryHeaderNav from './CommentaryHeaderNav.vue'

const props = defineProps<{
    path: string[]
    bookId?: number
    lineIndex?: number
    hasPrevious?: boolean
    hasNext?: boolean
}>()

const emit = defineEmits<{
    (e: 'click'): void
    (e: 'navigate-previous'): void
    (e: 'navigate-next'): void
    (e: 'navigate-previous-line'): void
    (e: 'navigate-next-line'): void
}>()

const displayPath = computed(() => props.path.join(' > '))

const toolbarRef = ref<HTMLElement>()
const showNav = useElementHover(toolbarRef)
</script>

<style scoped>
.commentary-toolbar {
    position: sticky;
    top: 0;
    background-color: var(--reading-bg-primary);
    padding: 8px 0;
    z-index: 10;
    display: flex;
    align-items: center;
    gap: 12px;
}

.commentary-toolbar-title {
    margin: 0;
    font-size: calc(1.1rem * var(--commentary-font-size) / 100);
    font-weight: 600;
    color: var(--reading-text-primary);
    direction: rtl;
    font-family: var(--commentary-header-font);
    flex: 1;
    min-width: 0;
}
</style>
