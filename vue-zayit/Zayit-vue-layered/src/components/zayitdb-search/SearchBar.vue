<template>
    <div class="bar search-bar">
        <div class="search-input-wrapper">
            <input ref="inputRef"
                   v-model="localQuery"
                   type="text"
                   class="search-input"
                   placeholder="חיפוש בכל הספרים..."
                   @keydown.enter="handleSearch"
                   @keydown.esc="handleClear" />
            <button @click="isSearching ? $emit('cancel') : handleSearch()"
                    class="search-button-inside search-button-left"
                    :class="{ 'is-searching': isSearching }"
                    :disabled="!isSearching && !localQuery.trim()"
                    :title="isSearching ? 'ביטול חיפוש' : 'חיפוש'">
                <div v-if="isSearching"
                     class="search-progress-container">
                    <svg class="progress-ring"
                         viewBox="0 0 24 24">
                        <circle class="progress-ring-bg"
                                cx="12"
                                cy="12"
                                r="10"
                                fill="none"
                                stroke-width="2" />
                        <circle class="progress-ring-spinner"
                                cx="12"
                                cy="12"
                                r="10"
                                fill="none"
                                stroke-width="2"
                                stroke-dasharray="31.4 31.4"
                                stroke-linecap="round" />
                    </svg>
                    <Icon icon="fluent:dismiss-24-regular"
                          class="cancel-icon" />
                </div>
                <Icon v-else
                      icon="fluent:search-24-regular" />
            </button>
            <button @click.stop="$emit('toggleFilter')"
                    class="search-button-inside search-button-right"
                    :class="{ 'filter-active': filterCount > 0 }"
                    :title="filterCount > 0 ? `סינון: ${filterCount} ספרים` : 'סינון לפי ספרים'">
                <Icon icon="fluent:filter-24-regular" />
            </button>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { Icon } from '@iconify/vue'

const props = defineProps<{
    searchQuery: string
    isSearching: boolean
    filterCount: number
}>()

const emit = defineEmits<{
    search: [query: string]
    cancel: []
    toggleFilter: []
    clear: []
    'update:searchQuery': [query: string]
}>()

const inputRef = ref<HTMLInputElement | null>(null)
const localQuery = ref(props.searchQuery)

// Sync local query with prop
watch(() => props.searchQuery, (newValue) => {
    localQuery.value = newValue
})

// Sync prop with local query
watch(localQuery, (newValue) => {
    emit('update:searchQuery', newValue)
})

const handleSearch = () => {
    if (localQuery.value.trim()) {
        emit('search', localQuery.value)
    }
}

const handleClear = () => {
    localQuery.value = ''
    emit('clear')
    inputRef.value?.focus()
}

// Expose focus method
defineExpose({
    focus: () => inputRef.value?.focus()
})
</script>

<style scoped>
.search-bar {
    padding: 12px;
    border-top: 1px solid var(--color-border);
}

.search-input-wrapper {
    position: relative;
    width: 100%;
}

.search-input {
    width: 100%;
    padding: 10px 48px 10px 48px;
    border: 1px solid var(--border-color);
    border-radius: 20px;
    background-color: var(--bg-primary);
    color: var(--text-primary);
    font-size: 15px;
    direction: rtl;
    text-align: right;
    transition: border-color 0.15s ease;
    height: 40px;
}

.search-input:focus {
    border-color: var(--accent-color);
    box-shadow: 0 0 0 0.5px var(--accent-color);
    outline: none;
}

.search-input::placeholder {
    color: var(--text-secondary);
    opacity: 1;
}

.search-button-inside {
    position: absolute;
    top: 50%;
    transform: translateY(-50%);
    padding: 6px;
    border-radius: 6px;
    background: transparent;
    border: none;
    color: var(--color-text-primary);
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    transition: background-color 0.15s ease, color 0.15s ease;
}

.search-button-left {
    left: 8px;
}

.search-button-right {
    right: 8px;
}

.search-button-inside:hover:not(:disabled) {
    background-color: var(--hover-bg);
    color: var(--accent-color);
}

.search-button-inside:disabled {
    opacity: 0.4;
    cursor: not-allowed;
}

.search-button-inside.is-searching {
    cursor: pointer;
}

.filter-active {
    color: var(--accent-color);
}

.search-progress-container {
    position: relative;
    width: 20px;
    height: 20px;
    display: flex;
    align-items: center;
    justify-content: center;
}

.cancel-icon {
    position: absolute;
    width: 14px;
    height: 14px;
    color: var(--text-secondary);
}

.progress-ring {
    width: 20px;
    height: 20px;
    animation: rotate 1s linear infinite;
}

.progress-ring-bg {
    stroke: var(--border-color);
}

.progress-ring-spinner {
    stroke: var(--accent-color);
    transform-origin: center;
}

@keyframes rotate {
    from {
        transform: rotate(0deg);
    }

    to {
        transform: rotate(360deg);
    }
}
</style>
