<template>
    <div v-if="isOpen"
         class="search-overlay"
         :style="{ top: topOffset }">
        <div class="search-bar bar flex-row">
            <input ref="searchInputRef"
                   v-model="searchQuery"
                   type="text"
                   class="flex-110"
                   placeholder="חיפוש..."
                   tabindex="0"
                   @keydown.enter.exact="findNext"
                   @keydown.shift.enter.prevent="findPrevious"
                   @keydown.esc="close" />
            <span class="search-count">{{ displayCount }}</span>
            <button @click.stop="findPrevious"
                    class="flex-center c-pointer search-btn touch-interactive"
                    :disabled="totalMatches === 0"
                    title="הקודם (Shift+Enter)">
                <Icon icon="fluent:chevron-left-28-regular"
                      class="search-icon"
                      style="transform: rotate(90deg);" />
            </button>
            <button @click.stop="findNext"
                    class="flex-center c-pointer search-btn touch-interactive"
                    :disabled="totalMatches === 0"
                    title="הבא (Enter)">
                <Icon icon="fluent:chevron-left-28-regular"
                      class="search-icon"
                      style="transform: rotate(-90deg);" />
            </button>
            <button @click.stop="close"
                    class="flex-center c-pointer search-btn close-btn touch-interactive"
                    title="סגור (Esc)">✕</button>
        </div>
    </div>
</template>

<script setup lang="ts">
import { Icon } from '@iconify/vue'
import { useGenericSearch } from '@/components/shared/useGenericSearch'

const props = withDefaults(defineProps<{
    isOpen: boolean
    topOffset?: string
    currentMatchIndex?: number
    totalMatches?: number
}>(), {
    topOffset: '8px',
    currentMatchIndex: -1,
    totalMatches: 0
})

const emit = defineEmits<{
    close: []
    search: [query: string]
    next: []
    previous: []
}>()

const {
    searchQuery,
    searchInputRef,
    displayCount,
    findNext,
    findPrevious,
    close
} = useGenericSearch(props, emit)
</script>

<style scoped>
.search-overlay {
    position: absolute;
    left: 50%;
    transform: translateX(-50%);
    z-index: 1000;
}

.search-bar {
    gap: 5px;
    padding: 7px 11px;
    background-color: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 4px;
    min-width: 320px;
    font-size: 13px;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
    opacity: 0.95;
}

.search-bar input {
    font-size: 13px;
    padding: 6px 8px;
    min-height: 28px;
}

.search-btn {
    padding: 3px;
    min-width: 28px;
    min-height: 28px;
    width: 28px;
    height: 28px;
}

.search-icon {
    width: 14px;
    height: 14px;
}

.close-btn {
    font-size: 14px;
    line-height: 1;
}

.search-count {
    padding: 0 7px;
    color: var(--text-secondary);
    white-space: nowrap;
    font-size: 12px;
}
</style>
