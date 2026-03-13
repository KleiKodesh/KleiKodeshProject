<template>
    <div class="commentary-header-nav">
        <button class="commentary-nav-btn c-pointer hover-bg"
                :title="showTree ? 'הסתר עץ מפרשים' : 'הצג עץ מפרשים'"
                @click="emit('toggle-tree')">
            <Icon :icon="showTree ? 'fluent:panel-right-28-filled' : 'fluent:panel-right-28-regular'" />
        </button>

        <div class="commentary-search-wrapper">
            <input ref="inputRef"
                   type="text"
                   class="commentary-search-input"
                   :list="`commentary-list-${componentId}`"
                   :placeholder="commentaryTitle || 'חפש מפרש...'"
                   @input="handleInput"
                   @change="handleSelect"
                   @keydown="handleKeydown"
                   @focus="handleFocus"
                   @blur="handleBlur" />
            <Icon icon="fluent:chevron-down-28-regular"
                  class="search-dropdown-icon" />
            <datalist :id="`commentary-list-${componentId}`">
                <option v-for="option in bookOptions"
                        :key="option.bookId"
                        :value="option.path">
                </option>
            </datalist>
        </div>

        <button class="commentary-nav-btn c-pointer hover-bg"
                :disabled="!hasPrevious"
                :title="hasPrevious ? 'מפרש קודם' : 'אין מפרש קודם'"
                @click="handleNavigatePrevious">
            <Icon icon="fluent:chevron-up-28-regular" />
        </button>
        <button class="commentary-nav-btn c-pointer hover-bg"
                :disabled="!hasNext"
                :title="hasNext ? 'מפרש הבא' : 'אין מפרש הבא'"
                @click="handleNavigateNext">
            <Icon icon="fluent:chevron-down-28-regular" />
        </button>

        <div class="nav-separator"></div>

        <button v-if="showBookButton"
                class="commentary-nav-btn c-pointer hover-bg"
                :title="`עבור לקטע בספר - ${commentaryTitle}`"
                @click="handleNavigateToBook">
            <Icon icon="fluent:book-open-24-regular" />
        </button>

        <div class="nav-separator"></div>

        <button class="commentary-nav-btn c-pointer hover-bg"
                :title="'קטע קודם'"
                @click="handleNavigatePreviousLine">
            <Icon icon="fluent:chevron-right-28-regular" />
        </button>
        <button class="commentary-nav-btn c-pointer hover-bg"
                :title="'קטע הבא'"
                @click="handleNavigateNextLine">
            <Icon icon="fluent:chevron-left-28-regular" />
        </button>
    </div>
</template>

<script setup lang="ts">
import { computed, watch, ref } from 'vue'
import { Icon } from '@iconify/vue'
import type { CommentaryTreeNode } from './useCommentaryTree'

const props = defineProps<{
    hasPrevious?: boolean
    hasNext?: boolean
    showBookButton?: boolean
    commentaryTitle?: string
    availableBooks?: CommentaryTreeNode[]
    commentaryGroups?: any[]
    connectionTypeId?: number
    showTree?: boolean
}>()

const emit = defineEmits<{
    (e: 'navigate-previous'): void
    (e: 'navigate-next'): void
    (e: 'navigate-previous-line'): void
    (e: 'navigate-next-line'): void
    (e: 'navigate-to-book'): void
    (e: 'select-commentary', bookId: number): void
    (e: 'select-commentary-with-filter', bookId: number, connectionTypeId: number): void
    (e: 'input-focus'): void
    (e: 'input-blur'): void
    (e: 'toggle-tree'): void
}>()

let uniqueId = 0
const componentId = ++uniqueId

const searchInput = ref('')
const inputRef = ref<HTMLInputElement>()

const bookOptions = computed(() => {
    return props.availableBooks?.map(book => ({
        bookId: book.bookId,
        path: book.path.join(' > ')
    })) || []
})

const filteredOptions = computed(() => {
    if (!searchInput.value) return bookOptions.value

    const search = searchInput.value.toLowerCase()
    return bookOptions.value.filter(option =>
        option.path.toLowerCase().includes(search)
    )
})

function handleNavigatePrevious() {
    emit('navigate-previous')
    emit('input-blur')
}

function handleNavigateNext() {
    emit('navigate-next')
    emit('input-blur')
}

function handleNavigatePreviousLine() {
    emit('navigate-previous-line')
    emit('input-blur')
}

function handleNavigateNextLine() {
    emit('navigate-next-line')
    emit('input-blur')
}

function handleNavigateToBook() {
    emit('navigate-to-book')
    emit('input-blur')
}

function handleInput(event: Event) {
    const input = event.target as HTMLInputElement
    searchInput.value = input.value
}

function handleKeydown(event: KeyboardEvent) {
    if (event.key === 'Enter') {
        event.preventDefault()

        // If there's exactly one filtered option, select it
        if (filteredOptions.value.length === 1) {
            const option = filteredOptions.value[0]
            if (option?.bookId) {
                // Get the connectionTypeId from the selected book's metadata
                const selectedGroup = props.commentaryGroups?.find(g => g.targetBookId === option.bookId)
                const nodeConnectionTypeId = selectedGroup?.connectionTypeId
                
                // If different filter, emit special event with filter info
                if (nodeConnectionTypeId !== undefined && nodeConnectionTypeId !== props.connectionTypeId) {
                    emit('select-commentary-with-filter', option.bookId, nodeConnectionTypeId)
                } else {
                    emit('select-commentary', option.bookId)
                }
                
                searchInput.value = ''
                if (inputRef.value) {
                    inputRef.value.value = ''
                    inputRef.value.blur()
                }
                // Emit event to move focus back to content
                emit('input-blur')
            }
        }
    } else if (event.key === 'Escape') {
        searchInput.value = ''
        if (inputRef.value) {
            inputRef.value.value = ''
            inputRef.value.blur()
        }
        // Emit event to move focus back to content
        emit('input-blur')
    }
}

function handleSelect(event: Event) {
    const input = event.target as HTMLInputElement
    const selectedPath = input.value

    if (!selectedPath) return

    // Find the book that matches the selected path
    const selectedOption = bookOptions.value.find(
        option => option.path === selectedPath
    )

    if (selectedOption?.bookId) {
        // Get the connectionTypeId from the selected book's metadata
        const selectedGroup = props.commentaryGroups?.find(g => g.targetBookId === selectedOption.bookId)
        const nodeConnectionTypeId = selectedGroup?.connectionTypeId
        
        // If different filter, emit special event with filter info
        if (nodeConnectionTypeId !== undefined && nodeConnectionTypeId !== props.connectionTypeId) {
            emit('select-commentary-with-filter', selectedOption.bookId, nodeConnectionTypeId)
        } else {
            emit('select-commentary', selectedOption.bookId)
        }
    }

    // Clear the input and move focus back
    searchInput.value = ''
    input.value = ''
    input.blur()
    emit('input-blur')
}

function handleFocus() {
    emit('input-focus')
}

function handleBlur() {
    // Delay to allow click on datalist option
    setTimeout(() => {
        emit('input-blur')
    }, 200)
}
</script>

<style scoped>
.commentary-header-nav {
    display: flex;
    gap: 4px;
    align-items: center;
    width: 100%;
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
    flex-shrink: 0;
}

.commentary-nav-btn:hover:not(:disabled) {
    color: var(--accent-color);
}

.commentary-nav-btn:disabled {
    opacity: 0.4;
    cursor: not-allowed;
}

.commentary-search-wrapper {
    margin-left: 4px;
    flex: 1;
    min-width: 0;
    position: relative;
}

.commentary-search-input {
    width: 100%;
    padding: 2px 6px 2px 24px;
    font-size: calc(0.9rem * var(--commentary-font-size) / 100);
    color: var(--text-primary);
    background-color: var(--input-bg);
    border: 1px solid var(--border-color);
    border-radius: 4px;
    direction: rtl;
    outline: none;
    appearance: none;
    -webkit-appearance: none;
    -moz-appearance: none;
}

.commentary-search-input::-webkit-calendar-picker-indicator {
    display: none;
    opacity: 0;
    width: 0;
    height: 0;
}

.commentary-search-input::-webkit-list-button {
    display: none;
}

.commentary-search-input::-moz-list-button {
    display: none;
}

.commentary-search-input:focus {
    border-color: var(--accent-color);
}

.commentary-search-input::placeholder {
    color: var(--text-secondary);
}

.search-dropdown-icon {
    position: absolute;
    left: 4px;
    top: 50%;
    transform: translateY(-50%);
    font-size: calc(0.8rem * var(--commentary-font-size) / 100);
    color: var(--text-secondary);
    pointer-events: none;
}
</style>
