<template>
    <div class="flex-row combobox-wrapper"
         ref="comboboxRef">
        <input type="text"
               class="combobox-input"
               v-model="searchText"
               @input="onInput"
               @focus="onFocus"
               @blur="onBlur"
               @keydown="onKeyDown"
               :placeholder="placeholder"
               :dir="dir"
               ref="inputRef" />
        <svg class="combobox-chevron"
             width="12"
             height="12"
             viewBox="0 0 24 24"
             fill="none"
             stroke="currentColor"
             stroke-width="2"
             @mousedown.prevent="toggleDropdown">
            <polyline points="6 9 12 15 18 9"></polyline>
        </svg>
        <div v-if="showDropdown && filteredOptions.length > 0"
             class="combobox-dropdown">
            <div v-for="(option, index) in filteredOptions"
                 :key="option.value"
                 class="c-pointer combobox-option"
                 @mousedown.prevent="selectOption(option.value)"
                 :class="{
                    active: option.value === modelValue,
                    highlighted: index === highlightedIndex
                }">
                {{ option.label }}
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'

// Simple debounce function
function debounce<T extends (...args: any[]) => any>(
    func: T,
    delay: number
): (...args: Parameters<T>) => void {
    let timeoutId: number | undefined
    return (...args: Parameters<T>) => {
        clearTimeout(timeoutId)
        timeoutId = window.setTimeout(() => func(...args), delay)
    }
}

export interface ComboboxOption {
    label: string
    value: string | number
}

const props = withDefaults(defineProps<{
    modelValue: string | number
    options: ComboboxOption[]
    placeholder?: string
    dir?: 'ltr' | 'rtl'
}>(), {
    placeholder: '',
    dir: 'rtl'
})

const emit = defineEmits<{
    'update:modelValue': [value: string | number]
}>()

const comboboxRef = ref<HTMLElement | null>(null)
const inputRef = ref<HTMLInputElement | null>(null)
const searchText = ref('')
const debouncedSearchText = ref('')
const showDropdown = ref(false)
const highlightedIndex = ref(-1)

const currentLabel = computed(() => {
    const option = props.options.find(opt => opt.value === props.modelValue)
    return option ? option.label : ''
})

const filteredOptions = computed(() => {
    const search = debouncedSearchText.value.trim().toLowerCase()

    if (search === '') {
        return props.options
    }

    const searchWords = search.split(/\s+/).filter(word => word.length > 0)
    return props.options.filter(option => {
        const labelLower = option.label.toLowerCase()
        return searchWords.every(word => labelLower.includes(word))
    })
})

// Debounced function to update search text
const updateDebouncedSearch = debounce((value: string) => {
    debouncedSearchText.value = value
}, 150)

const onInput = () => {
    showDropdown.value = true
    highlightedIndex.value = -1
    updateDebouncedSearch(searchText.value)
}

const onFocus = (event: FocusEvent) => {
    showDropdown.value = true
    const input = event.target as HTMLInputElement
    if (input) {
        input.select()
    }
}

const onBlur = () => {
    setTimeout(() => {
        showDropdown.value = false
        searchText.value = ''
        debouncedSearchText.value = ''
        highlightedIndex.value = -1
    }, 200)
}

const toggleDropdown = () => {
    if (showDropdown.value) {
        showDropdown.value = false
        searchText.value = ''
        debouncedSearchText.value = ''
        highlightedIndex.value = -1
        inputRef.value?.blur()
    } else {
        showDropdown.value = true
        inputRef.value?.focus()
    }
}

const selectOption = (value: string | number) => {
    emit('update:modelValue', value)
    searchText.value = ''
    debouncedSearchText.value = ''
    showDropdown.value = false
    highlightedIndex.value = -1
    inputRef.value?.blur()
}

const onKeyDown = (event: KeyboardEvent) => {
    if (!showDropdown.value && (event.key === 'ArrowDown' || event.key === 'ArrowUp')) {
        showDropdown.value = true
        event.preventDefault()
        return
    }

    if (!showDropdown.value) return

    switch (event.key) {
        case 'ArrowDown':
            event.preventDefault()
            highlightedIndex.value = Math.min(highlightedIndex.value + 1, filteredOptions.value.length - 1)
            scrollToHighlighted()
            break

        case 'ArrowUp':
            event.preventDefault()
            highlightedIndex.value = Math.max(highlightedIndex.value - 1, -1)
            scrollToHighlighted()
            break

        case 'Enter':
            event.preventDefault()
            if (highlightedIndex.value >= 0 && highlightedIndex.value < filteredOptions.value.length) {
                const option = filteredOptions.value[highlightedIndex.value]
                if (option) {
                    selectOption(option.value)
                }
            } else if (filteredOptions.value.length === 1) {
                const option = filteredOptions.value[0]
                if (option) {
                    selectOption(option.value)
                }
            }
            break

        case 'Escape':
            event.preventDefault()
            showDropdown.value = false
            searchText.value = ''
            debouncedSearchText.value = ''
            highlightedIndex.value = -1
            inputRef.value?.blur()
            break
    }
}

const scrollToHighlighted = () => {
    if (highlightedIndex.value < 0) return

    setTimeout(() => {
        const dropdown = comboboxRef.value?.querySelector('.combobox-dropdown')
        const highlighted = dropdown?.querySelector('.combobox-option.highlighted')
        if (dropdown && highlighted && highlighted instanceof HTMLElement) {
            const dropdownRect = dropdown.getBoundingClientRect()
            const highlightedRect = highlighted.getBoundingClientRect()

            if (highlightedRect.bottom > dropdownRect.bottom) {
                highlighted.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
            } else if (highlightedRect.top < dropdownRect.top) {
                highlighted.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
            }
        }
    }, 0)
}

watch(() => props.modelValue, () => {
    searchText.value = ''
    debouncedSearchText.value = ''
})
</script>

<style scoped>
.combobox-wrapper {
    position: relative;
}

.combobox-input {
    width: 100%;
    padding: 3px 6px 3px 24px;
    background: var(--bg-primary);
    border: 1px solid var(--border-color);
    border-radius: 3px;
    color: var(--text-primary);
    font-size: 12px;
    text-align: right;
    height: 24px;
    line-height: 1;
}

.combobox-input:focus {
    outline: none;
    border-color: var(--accent-color);
}

.combobox-input::placeholder {
    color: var(--text-primary);
    opacity: 1;
}

.combobox-chevron {
    position: absolute;
    left: 6px;
    color: var(--text-secondary);
    opacity: 0.6;
}

.combobox-wrapper:hover .combobox-chevron {
    opacity: 1;
}

.combobox-input:focus~.combobox-chevron {
    transform: rotate(180deg);
    opacity: 1;
}

.combobox-dropdown {
    position: absolute;
    top: 100%;
    left: 0;
    right: 0;
    margin-top: 2px;
    background: var(--bg-primary);
    border: 1px solid var(--border-color);
    border-radius: 4px;
    max-height: 200px;
    overflow-y: auto;
    z-index: 1001;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

.combobox-option {
    padding: 5px 8px;
    color: var(--text-primary);
    font-size: 12px;
    text-align: right;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}

.combobox-option:hover,
.combobox-option.highlighted {
    background: var(--hover-bg);
}

.combobox-option.active {
    background: var(--accent-bg);
    color: white;
}

.combobox-option.active.highlighted {
    opacity: 0.8;
}
</style>
