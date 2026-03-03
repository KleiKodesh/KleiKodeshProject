import { ref, computed, watch } from 'vue';
import { useEventListener } from '@vueuse/core';
import { scrollToElementCenter } from '@/components/shared/useScrollToElement';

export interface ComboboxOption {
    label: string;
    value: string | number;
    isSeparator?: boolean;
    separatorTitle?: string;
}

// Simple debounce function
function debounce<T extends (...args: any[]) => any>(
    func: T,
    delay: number
): (...args: Parameters<T>) => void {
    let timeoutId: number | undefined;
    return (...args: Parameters<T>) => {
        clearTimeout(timeoutId);
        timeoutId = window.setTimeout(() => func(...args), delay);
    };
}

export function useCombobox(
    props: {
        modelValue: string | number;
        options: ComboboxOption[];
    },
    emit: (event: 'update:modelValue', value: string | number) => void
) {
    const comboboxRef = ref<HTMLElement | null>(null);
    const inputRef = ref<HTMLInputElement | null>(null);
    const dropdownRef = ref<HTMLElement | null>(null);
    const searchText = ref('');
    const debouncedSearchText = ref('');
    const showDropdown = ref(false);
    const highlightedIndex = ref(-1);
    const isMouseDownOnDropdown = ref(false);
    const isFocused = ref(false);
    const dropdownWidth = ref<number>(0);

    const currentLabel = computed(() => {
        const option = props.options.find(opt => opt.value === props.modelValue);
        return option ? option.label : '';
    });

    const displayText = computed({
        get: () => {
            if (isFocused.value) {
                return searchText.value;
            }
            return currentLabel.value;
        },
        set: (value: string) => {
            searchText.value = value;
        }
    });

    const filteredOptions = computed(() => {
        const search = debouncedSearchText.value.trim().toLowerCase();

        if (search === '') {
            return props.options;
        }

        const searchWords = search.split(/\s+/).filter(word => word.length > 0);
        return props.options.filter(option => {
            // Always include separators
            if (option.isSeparator) {
                return true;
            }

            const labelLower = option.label.toLowerCase();
            return searchWords.every(word => labelLower.includes(word));
        });
    });

    const dropdownStyle = computed(() => {
        const inputWidth = comboboxRef.value?.offsetWidth || 0;
        const width = Math.max(dropdownWidth.value, inputWidth);
        return {
            width: `${Math.min(width, 400)}px`
        };
    });

    // Calculate width based on currently visible items
    function updateDropdownWidth() {
        if (!dropdownRef.value) return;

        const dropdown = dropdownRef.value;
        const dropdownRect = dropdown.getBoundingClientRect();

        // Get all option elements
        const options = dropdown.querySelectorAll('.combobox-option');

        let maxVisibleWidth = 0;

        // Create a temporary element to measure natural text width
        const temp = document.createElement('div');
        temp.style.position = 'absolute';
        temp.style.visibility = 'hidden';
        temp.style.whiteSpace = 'nowrap';
        temp.style.padding = '6px 10px';
        temp.style.fontSize = '12px';
        temp.style.fontFamily = getComputedStyle(dropdown).fontFamily;
        document.body.appendChild(temp);

        options.forEach((option) => {
            const optionRect = option.getBoundingClientRect();

            // Check if option is visible in the dropdown viewport
            const isVisible =
                optionRect.bottom > dropdownRect.top &&
                optionRect.top < dropdownRect.bottom;

            if (isVisible && option instanceof HTMLElement) {
                // Measure the natural width of the text content
                temp.textContent = option.textContent || '';
                const naturalWidth = temp.offsetWidth;
                if (naturalWidth > maxVisibleWidth) {
                    maxVisibleWidth = naturalWidth;
                }
            }
        });

        document.body.removeChild(temp);

        // Add a small buffer for scrollbar (if present)
        const scrollbarWidth = dropdown.offsetWidth - dropdown.clientWidth;
        maxVisibleWidth += scrollbarWidth;

        dropdownWidth.value = maxVisibleWidth;
    }

    // Scroll handler - update width immediately
    function onDropdownScroll() {
        updateDropdownWidth();
    }

    // Keyboard navigation using useEventListener
    useEventListener('keydown', (event: KeyboardEvent) => {
        if (!showDropdown.value) return;

        // Arrow Down - move highlight down
        if (event.code === 'ArrowDown') {
            event.preventDefault();
            let nextIndex = highlightedIndex.value + 1;
            // Skip separators
            while (nextIndex < filteredOptions.value.length && filteredOptions.value[nextIndex]?.isSeparator) {
                nextIndex++;
            }
            highlightedIndex.value = Math.min(nextIndex, filteredOptions.value.length - 1);
            scrollToHighlighted();
        }

        // Arrow Up - move highlight up
        if (event.code === 'ArrowUp') {
            event.preventDefault();
            let prevIndex = highlightedIndex.value - 1;
            // Skip separators
            while (prevIndex >= 0 && filteredOptions.value[prevIndex]?.isSeparator) {
                prevIndex--;
            }
            highlightedIndex.value = Math.max(prevIndex, -1);
            scrollToHighlighted();
        }

        // Enter - select highlighted option
        if (event.code === 'Enter') {
            event.preventDefault();
            if (highlightedIndex.value >= 0 && highlightedIndex.value < filteredOptions.value.length) {
                const option = filteredOptions.value[highlightedIndex.value];
                if (option && !option.isSeparator) {
                    selectOption(option.value);
                }
            } else {
                // If no highlight, select first non-separator option
                const firstOption = filteredOptions.value.find(opt => !opt.isSeparator);
                if (firstOption) {
                    selectOption(firstOption.value);
                }
            }
        }

        // Escape - close dropdown
        if (event.code === 'Escape') {
            event.preventDefault();
            isFocused.value = false;
            showDropdown.value = false;
            searchText.value = '';
            debouncedSearchText.value = '';
            highlightedIndex.value = -1;
            inputRef.value?.blur();
        }
    });

    const updateDebouncedSearch = debounce((value: string) => {
        debouncedSearchText.value = value;
    }, 150);

    const onInput = () => {
        showDropdown.value = true;
        highlightedIndex.value = -1;
        updateDebouncedSearch(searchText.value);
    };

    const onFocus = (event: FocusEvent) => {
        isFocused.value = true;
        showDropdown.value = true;
        const input = event.target as HTMLInputElement;
        if (input) {
            if (searchText.value === '') {
                input.select();
            }
        }

        // Scroll to currently selected item when dropdown opens
        scrollToSelected();

        // Setup scroll listener and initial width calculation
        setTimeout(() => {
            if (dropdownRef.value) {
                dropdownRef.value.addEventListener('scroll', onDropdownScroll, { passive: true });
                updateDropdownWidth();
            }
        }, 50);
    };

    const onBlur = (event: FocusEvent) => {
        if (isMouseDownOnDropdown.value) {
            setTimeout(() => inputRef.value?.focus(), 0);
            return;
        }

        setTimeout(() => {
            isFocused.value = false;
            showDropdown.value = false;
            searchText.value = '';
            debouncedSearchText.value = '';
            highlightedIndex.value = -1;

            // Cleanup scroll listener
            if (dropdownRef.value) {
                dropdownRef.value.removeEventListener('scroll', onDropdownScroll);
            }
        }, 200);
    };

    const toggleDropdown = () => {
        if (showDropdown.value) {
            isFocused.value = false;
            showDropdown.value = false;
            searchText.value = '';
            debouncedSearchText.value = '';
            highlightedIndex.value = -1;
            inputRef.value?.blur();

            // Cleanup scroll listener
            if (dropdownRef.value) {
                dropdownRef.value.removeEventListener('scroll', onDropdownScroll);
            }
        } else {
            showDropdown.value = true;
            inputRef.value?.focus();

            // Scroll to currently selected item when dropdown opens
            scrollToSelected();

            // Setup scroll listener and initial width calculation
            setTimeout(() => {
                if (dropdownRef.value) {
                    dropdownRef.value.addEventListener('scroll', onDropdownScroll, { passive: true });
                    updateDropdownWidth();
                }
            }, 50);
        }
    };

    const selectOption = (value: string | number) => {
        emit('update:modelValue', value);
        searchText.value = '';
        debouncedSearchText.value = '';
        showDropdown.value = false;
        highlightedIndex.value = -1;
        inputRef.value?.blur();
    };

    const onKeyDown = (event: KeyboardEvent) => {
        // Open dropdown when pressing arrow keys while closed
        if (!showDropdown.value && (event.code === 'ArrowDown' || event.code === 'ArrowUp')) {
            showDropdown.value = true;
            event.preventDefault();

            // Scroll to currently selected item when dropdown opens
            scrollToSelected();

            // Setup scroll listener and initial width calculation
            setTimeout(() => {
                if (dropdownRef.value) {
                    dropdownRef.value.addEventListener('scroll', onDropdownScroll, { passive: true });
                    updateDropdownWidth();
                }
            }, 50);
            return;
        }
    };

    const scrollToSelected = () => {
        // Wait for dropdown to render
        setTimeout(async () => {
            const dropdown = dropdownRef.value;
            const activeOption = dropdown?.querySelector('.combobox-option.active');

            if (dropdown && activeOption && activeOption instanceof HTMLElement) {
                await scrollToElementCenter(activeOption, { behavior: 'instant' });
            }
        }, 0);
    };

    const scrollToHighlighted = () => {
        if (highlightedIndex.value < 0) return;

        setTimeout(async () => {
            const dropdown = dropdownRef.value;
            const highlighted = dropdown?.querySelector('.combobox-option.highlighted');
            if (dropdown && highlighted && highlighted instanceof HTMLElement) {
                const dropdownRect = dropdown.getBoundingClientRect();
                const highlightedRect = highlighted.getBoundingClientRect();

                if (highlightedRect.bottom > dropdownRect.bottom || highlightedRect.top < dropdownRect.top) {
                    await scrollToElementCenter(highlighted, { behavior: 'instant' });
                }
            }
        }, 0);
    };

    watch(() => props.modelValue, (newValue, oldValue) => {
        if (newValue !== oldValue && !showDropdown.value) {
            searchText.value = '';
            debouncedSearchText.value = '';
        }
    });

    return {
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
    };
}
