<template>
    <div class="flex-row combobox-wrapper"
         ref="comboboxRef">
        <input type="text"
               class="combobox-input"
               v-model="displayText"
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
             class="combobox-dropdown"
             :style="dropdownStyle"
             ref="dropdownRef"
             @mousedown="isMouseDownOnDropdown = true"
             @mouseup="isMouseDownOnDropdown = false"
             @mouseleave="isMouseDownOnDropdown = false">
            <template v-for="(option, index) in filteredOptions"
                      :key="option.value">
                <div v-if="option.isSeparator"
                     class="combobox-separator"
                     :class="{ 'with-title': option.separatorTitle }">
                    <span v-if="option.separatorTitle"
                          class="separator-title">{{ option.separatorTitle }}</span>
                </div>
                <div v-else
                     class="c-pointer combobox-option"
                     @mousedown.prevent="selectOption(option.value)"
                     :class="{
                        active: option.value === modelValue,
                        highlighted: index === highlightedIndex
                    }"
                     :data-index="index">
                    {{ option.label }}
                </div>
            </template>
        </div>
    </div>
</template>

<script setup lang="ts">
import { useCombobox, type ComboboxOption } from '@/components/shared/useCombobox'

/**
 * Reusable Combobox component with search, keyboard navigation, and separator support
 * 
 * Usage with separators:
 * 
 * const options: ComboboxOption[] = [
 *   { label: 'Option 1', value: 1 },
 *   { label: 'Option 2', value: 2 },
 *   { label: '', value: 'sep1', isSeparator: true }, // Simple separator
 *   { label: 'Option 3', value: 3 },
 *   { label: '', value: 'sep2', isSeparator: true, separatorTitle: 'Category' }, // Separator with title
 *   { label: 'Option 4', value: 4 },
 * ]
 */

export type { ComboboxOption }

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

const {
    comboboxRef,
    inputRef,
    dropdownRef,
    displayText,
    showDropdown,
    filteredOptions,
    dropdownStyle,
    highlightedIndex,
    isMouseDownOnDropdown,
    onInput,
    onFocus,
    onBlur,
    toggleDropdown,
    selectOption,
    onKeyDown
} = useCombobox(props, emit)
</script>

<style scoped>
.combobox-wrapper {
    position: relative;
}

.combobox-input {
    width: 100%;
    padding: 4px 6px 4px 24px;
    background: var(--bg-primary);
    border: 1px solid var(--border-color);
    border-radius: 3px;
    color: var(--text-primary);
    font-size: 12px;
    text-align: right;
    min-height: 28px;
    line-height: 1.3;
    touch-action: manipulation;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}

@media (hover: none) and (pointer: coarse) {
    .combobox-input {
        min-height: 32px;
        padding: 6px 8px 6px 28px;
    }
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
    top: 50%;
    transform: translateY(-50%);
    color: var(--text-secondary);
    opacity: 0.6;
    width: 12px;
    height: 12px;
    pointer-events: auto;
    cursor: pointer;
    touch-action: manipulation;
}

.combobox-wrapper:hover .combobox-chevron {
    opacity: 1;
}

.combobox-input:focus~.combobox-chevron {
    transform: translateY(-50%) rotate(180deg);
    opacity: 1;
}

.combobox-dropdown {
    position: absolute;
    top: 100%;
    right: 0;
    left: auto;
    margin-top: 2px;
    background: var(--bg-primary);
    border: 1px solid var(--border-color);
    border-radius: 4px;
    max-height: 200px;
    overflow-y: auto;
    overflow-x: hidden;
    z-index: 1001;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

.combobox-option {
    padding: 6px 10px;
    color: var(--text-primary);
    font-size: 12px;
    text-align: right;
    white-space: nowrap;
    min-height: 32px;
    display: flex;
    align-items: center;
    touch-action: manipulation;
}

@media (hover: none) and (pointer: coarse) {
    .combobox-option {
        min-height: 36px;
        padding: 8px 12px;
    }
}

.combobox-option:hover,
.combobox-option.highlighted {
    background: var(--hover-bg);
}

.combobox-option.active {
    background: var(--accent-color);
    color: white;
}

.combobox-option.active.highlighted {
    opacity: 0.9;
}

.combobox-separator {
    height: 1px;
    background: var(--border-color);
    margin: 4px 0;
}

.combobox-separator.with-title {
    height: auto;
    background: none;
    margin: 8px 0 4px 0;
    padding: 0 10px;
}

.separator-title {
    display: block;
    font-size: 11px;
    font-weight: 600;
    color: var(--text-secondary);
    text-transform: uppercase;
    letter-spacing: 0.5px;
    text-align: right;
    padding-bottom: 4px;
    border-bottom: 1px solid var(--border-color);
}
</style>
