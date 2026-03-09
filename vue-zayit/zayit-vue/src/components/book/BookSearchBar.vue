<template>
    <div v-if="isOpen"
         ref="searchOverlayRef"
         class="search-overlay"
         :style="overlayStyle">
        <div class="search-bar flex-row">
            <div ref="dragHandleRef"
                 class="drag-handle flex-center"
                 title="גרור להזזה">
                <Icon icon="fluent:re-order-dots-vertical-20-regular"
                      class="drag-icon" />
            </div>

            <input ref="searchInputRef"
                   v-model="localQuery"
                   type="text"
                   class="flex-110"
                   :placeholder="searchPlaceholder"
                   tabindex="0"
                   @keydown.esc="handleClose"
                   @keydown.enter.prevent="handleEnter"
                   @keydown.alt.prevent="cycleSearchScope"
                   @input="handleInput" />

            <span v-if="totalMatches > 0"
                  class="match-count">
                {{ currentMatchIndex + 1 }} / {{ totalMatches }}
            </span>

            <div v-if="props.isCommentaryVisible"
                 class="scope-dropdown-container">
                <button ref="scopeToggleRef"
                        @click.stop="toggleScopeDropdown"
                        class="flex-center c-pointer search-btn scope-toggle-btn touch-interactive"
                        :class="{ 'scope-active': localSearchScope !== 'lines' }"
                        :title="`${scopeTooltip} (Alt)`">
                    <Icon icon="fluent:filter-28-filled"
                          class="search-icon" />
                </button>

                <div v-if="isScopeDropdownOpen"
                     ref="scopeDropdownRef"
                     class="scope-dropdown">
                    <button @click.stop="selectScope('lines')"
                            class="scope-option"
                            :class="{ 'active': localSearchScope === 'lines' }">
                        טקסט
                    </button>
                    <button @click.stop="selectScope('commentary')"
                            class="scope-option"
                            :class="{ 'active': localSearchScope === 'commentary' }">
                        מפרשים
                    </button>
                    <button @click.stop="selectScope('both')"
                            class="scope-option"
                            :class="{ 'active': localSearchScope === 'both' }">
                        הכל
                    </button>
                </div>
            </div>

            <button @click.stop="handlePrevious"
                    class="flex-center c-pointer search-btn touch-interactive"
                    :disabled="totalMatches === 0"
                    title="קודם (Shift+Enter)">
                <Icon icon="fluent:chevron-left-28-regular"
                      class="search-icon"
                      style="transform: rotate(90deg);" />
            </button>

            <button @click.stop="handleNext"
                    class="flex-center c-pointer search-btn touch-interactive"
                    :disabled="totalMatches === 0"
                    title="הבא (Enter)">
                <Icon icon="fluent:chevron-left-28-regular"
                      class="search-icon"
                      style="transform: rotate(-90deg);" />
            </button>

            <button @click.stop="handleClose"
                    class="flex-center c-pointer search-btn close-btn touch-interactive"
                    title="סגור (Esc)">✕</button>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, watch, nextTick, onMounted, computed } from 'vue'
import { useDebounceFn, useLocalStorage, useDraggable, onClickOutside } from '@vueuse/core'
import { Icon } from '@iconify/vue'

const props = withDefaults(defineProps<{
    isOpen: boolean
    topOffset?: string
    currentMatchIndex?: number
    totalMatches?: number
    searchScope?: 'lines' | 'commentary' | 'both'
    isCommentaryVisible?: boolean
}>(), {
    topOffset: '8px',
    currentMatchIndex: 0,
    totalMatches: 0,
    searchScope: 'lines',
    isCommentaryVisible: false
})

const emit = defineEmits<{
    close: []
    search: [query: string]
    next: []
    previous: []
    scopeChange: [scope: 'lines' | 'commentary' | 'both']
}>()

const searchInputRef = ref<HTMLInputElement | null>(null)
const searchOverlayRef = ref<HTMLElement | null>(null)
const dragHandleRef = ref<HTMLElement | null>(null)
const scopeToggleRef = ref<HTMLElement | null>(null)
const scopeDropdownRef = ref<HTMLElement | null>(null)
const localQuery = ref('')
const localSearchScope = ref<'lines' | 'commentary' | 'both'>(props.searchScope)
const isScopeDropdownOpen = ref(false)

// Watch for prop changes and sync local scope
watch(() => props.searchScope, (newScope) => {
    localSearchScope.value = newScope
})

// Computed placeholder based on search scope
const searchPlaceholder = computed(() => {
    switch (localSearchScope.value) {
        case 'commentary':
            return 'חיפוש במפרשים...'
        case 'both':
            return 'חיפוש בטקסט ומפרשים...'
        default:
            return 'חיפוש בטקסט...'
    }
})

// Computed tooltip for scope toggle button
const scopeTooltip = computed(() => {
    switch (localSearchScope.value) {
        case 'commentary':
            return 'חיפוש: מפרשים'
        case 'both':
            return 'חיפוש: הכל'
        default:
            return 'חיפוש: טקסט'
    }
})

// Close dropdown when clicking outside
onClickOutside(scopeDropdownRef, () => {
    isScopeDropdownOpen.value = false
}, { ignore: [scopeToggleRef] })

// Persistent position
const savedPosition = useLocalStorage<{ x: number; y: number }>('search-bar-position', {
    x: 0,
    y: 0
})

// Check if we have a saved position
const hasSavedPosition = computed(() => savedPosition.value.x !== 0 || savedPosition.value.y !== 0)

// Drag functionality using VueUse
useDraggable(searchOverlayRef, {
    handle: dragHandleRef,
    preventDefault: true,
    stopPropagation: true,
    initialValue: computed(() => {
        if (hasSavedPosition.value) {
            return { x: savedPosition.value.x, y: savedPosition.value.y }
        }
        // Default centered position
        const centerX = (window.innerWidth - 400) / 2 // approximate width
        return { x: centerX, y: 8 }
    }),
    onMove: (position) => {
        savedPosition.value = { x: position.x, y: position.y }
    },
    onEnd: (position) => {
        savedPosition.value = { x: position.x, y: position.y }
    }
})

const overlayStyle = computed(() => {
    const x = hasSavedPosition.value ? savedPosition.value.x : (window.innerWidth - 400) / 2
    const y = hasSavedPosition.value ? savedPosition.value.y : 8

    return {
        position: 'fixed' as const,
        left: `${x}px`,
        top: `${y}px`,
        margin: '0',
        zIndex: '1000',
    }
})

// Debounced search - 300ms delay
const debouncedSearch = useDebounceFn((query: string) => {
    emit('search', query)
}, 300)

function handleInput() {
    debouncedSearch(localQuery.value)
}

function handleEnter(event: KeyboardEvent) {
    if (event.shiftKey) {
        handlePrevious()
    } else {
        handleNext()
    }
}

function handleNext() {
    if (props.totalMatches > 0) {
        emit('next')
    }
}

function handlePrevious() {
    if (props.totalMatches > 0) {
        emit('previous')
    }
}

function handleClose() {
    localQuery.value = ''
    emit('search', '') // Clear search
    emit('close')
}

function toggleScopeDropdown() {
    isScopeDropdownOpen.value = !isScopeDropdownOpen.value
}

function selectScope(scope: 'lines' | 'commentary' | 'both') {
    localSearchScope.value = scope
    isScopeDropdownOpen.value = false
    emit('scopeChange', localSearchScope.value)
    // Re-run search with new scope if there's a query
    if (localQuery.value.trim().length >= 2) {
        emit('search', localQuery.value)
    }
}

// Debounced Alt key handler to prevent firing on Alt+Shift (language change) or other Alt combinations
const debouncedCycleSearchScope = useDebounceFn(() => {
    // Cycle through: lines → commentary → both → lines
    if (localSearchScope.value === 'lines') {
        localSearchScope.value = 'commentary'
    } else if (localSearchScope.value === 'commentary') {
        localSearchScope.value = 'both'
    } else {
        localSearchScope.value = 'lines'
    }

    emit('scopeChange', localSearchScope.value)
    // Re-run search with new scope if there's a query
    if (localQuery.value.trim().length >= 2) {
        emit('search', localQuery.value)
    }
}, 100)

function cycleSearchScope() {
    debouncedCycleSearchScope()
}

watch(() => props.isOpen, async (isOpen) => {
    if (isOpen) {
        await nextTick()

        // Center on first open if no saved position
        if (!hasSavedPosition.value && searchOverlayRef.value) {
            const rect = searchOverlayRef.value.getBoundingClientRect()
            const centerX = (window.innerWidth - rect.width) / 2
            const topY = 8
            savedPosition.value = { x: centerX, y: topY }
        }

        searchInputRef.value?.focus()
        searchInputRef.value?.select()
    } else {
        localQuery.value = ''
    }
})

onMounted(() => {
    if (props.isOpen) {
        searchInputRef.value?.focus()
    }
})
</script>

<style scoped>
.search-overlay {
    user-select: none;
}

.search-bar {
    gap: 3px;
    padding: 5px 8px;
    background-color: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 4px;
    min-width: 380px;
    font-size: 13px;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
    opacity: 0.95;
}

.drag-handle {
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 0;
    margin: 0;
    cursor: grab;
    color: var(--text-secondary);
    transition: color 0.15s ease;
    touch-action: none;
    flex-shrink: 0;
    width: 1.25rem;
}

.drag-handle:hover {
    color: var(--text-primary);
}

.drag-handle:active {
    cursor: grabbing;
}

.drag-icon {
    width: 12px;
    height: 12px;
}

.search-bar input {
    font-size: 13px;
    padding: 6px 8px;
    min-height: 28px;
}

.match-count {
    padding: 0 4px;
    color: var(--text-secondary);
    white-space: nowrap;
    font-size: 12px;
}

.scope-dropdown-container {
    position: relative;
}

.scope-toggle-btn {
    transition: color 0.15s ease;
}

.scope-toggle-btn.scope-active {
    color: var(--accent-color);
}

.scope-dropdown {
    position: absolute;
    top: calc(100% + 4px);
    right: 0;
    background-color: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 4px;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
    z-index: 1001;
    min-width: 100px;
    overflow: hidden;
}

.scope-option {
    display: block;
    width: 100%;
    padding: 8px 12px;
    text-align: right;
    background: transparent;
    border: none;
    color: var(--text-primary);
    cursor: pointer;
    font-size: 13px;
    transition: background-color 0.15s ease;
}

.scope-option:hover {
    background-color: var(--hover-bg);
}

.scope-option.active {
    background-color: var(--accent-color);
    color: white;
}

.search-btn {
    width: 2rem;
    height: 2rem;
    padding: 0.375rem;
}

.search-icon {
    width: 1.25rem;
    height: 1.25rem;
}

.close-btn {
    font-size: 1rem;
    line-height: 1;
}
</style>
