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
                   @keydown.enter="handleEnter"
                   @keydown.shift.enter.prevent="findPrevious"
                   @keydown.esc="close" />
            <span class="search-count">{{ currentMatchIndex + 1 }}/{{
                totalMatches }}</span>
            <button @click.stop="findPrevious"
                    class="flex-center c-pointer search-btn"
                    title="הקודם (Shift+Enter)">
                <Icon icon="fluent:chevron-left-28-regular"
                      class="search-icon"
                      style="transform: rotate(90deg);" />
            </button>
            <button @click.stop="findNext"
                    class="flex-center c-pointer search-btn"
                    title="הבא (Enter)">
                <Icon icon="fluent:chevron-left-28-regular"
                      class="search-icon"
                      style="transform: rotate(-90deg);" />
            </button>
            <button @click.stop="close"
                    class="flex-center c-pointer search-btn close-btn"
                    title="סגור (Esc)">✕</button>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, watch, nextTick } from 'vue'
import { Icon } from '@iconify/vue'

const props = withDefaults(defineProps<{
    isOpen: boolean
    topOffset?: string
}>(), {
    topOffset: '8px'
})

const emit = defineEmits<{
    close: []
    searchQueryChange: [query: string]
    navigateToMatch: [index: number]
}>()

const searchQuery = ref('')
const searchInputRef = ref<HTMLInputElement | null>(null)
const currentMatchIndex = ref(-1)
const totalMatches = ref(0)
let debounceTimeout: number | null = null

watch(() => props.isOpen, async (isOpen) => {
    if (isOpen) {
        await nextTick()
        searchInputRef.value?.focus()
        searchInputRef.value?.select()
    } else {
        searchQuery.value = ''
        currentMatchIndex.value = -1
        totalMatches.value = 0
    }
})

watch(searchQuery, (query) => {
    if (debounceTimeout !== null) {
        clearTimeout(debounceTimeout)
    }
    debounceTimeout = window.setTimeout(() => {
        emit('searchQueryChange', query)
    }, 300)
})

function setMatches(count: number) {
    totalMatches.value = count
    if (count > 0) {
        currentMatchIndex.value = 0
        emit('navigateToMatch', 0)
    } else {
        currentMatchIndex.value = -1
    }
}

function findNext() {
    if (totalMatches.value === 0) return
    currentMatchIndex.value = (currentMatchIndex.value + 1) % totalMatches.value
    emit('navigateToMatch', currentMatchIndex.value)
}

function handleEnter(e: KeyboardEvent) {
    // Only handle regular Enter (not Shift+Enter)
    if (!e.shiftKey) {
        findNext()
    }
}

function findPrevious() {
    if (totalMatches.value === 0) return
    currentMatchIndex.value = currentMatchIndex.value <= 0
        ? totalMatches.value - 1
        : currentMatchIndex.value - 1
    emit('navigateToMatch', currentMatchIndex.value)
}

function close() {
    emit('close')
}

defineExpose({
    setMatches
})
</script>

<style scoped>
.search-overlay {
    position: absolute;
    left: 50%;
    transform: translateX(-50%);
    z-index: 1000;
}

.search-bar {
    gap: 4px;
    padding: 6px 10px;
    background-color: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 4px;
    min-width: 320px;
    font-size: 13px;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
}

.search-bar input {
    font-size: 13px;
    padding: 4px 6px;
}

.search-btn {
    padding: 2px;
    width: 20px;
    height: 20px;
}

.search-icon {
    width: 10px;
    height: 10px;
}

.close-btn {
    font-size: 13px;
    line-height: 1;
}

.search-count {
    padding: 0 6px;
    color: var(--text-secondary);
    white-space: nowrap;
    font-size: 12px;
}
</style>
