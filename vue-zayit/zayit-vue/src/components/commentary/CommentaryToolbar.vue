<template>
    <div class="flex-between bar commentary-header">
        <!-- Spacer for layout balance -->
        <div style="width: 32px;"></div>

        <!-- Centered Navigation Controls -->
        <div class="flex-row flex-center commentary-navigation">
            <!-- Previous/Next Line Buttons -->
            <button class="flex-center c-pointer nav-btn"
                    :disabled="!canNavigateToPreviousLine || isNavigatingToLine"
                    @click.stop="$emit('navigate-previous-line')"
                    :title="isNavigatingToLine ? 'מחפש...' : 'שורה קודמת'">
                <Icon icon="fluent:chevron-right-28-regular"
                      class="small-icon" />
            </button>

            <button class="flex-center c-pointer nav-btn"
                    :disabled="!canNavigateToNextLine || isNavigatingToLine"
                    @click.stop="$emit('navigate-next-line')"
                    :title="isNavigatingToLine ? 'מחפש...' : 'שורה הבאה'">
                <Icon icon="fluent:chevron-left-28-regular"
                      class="small-icon" />
            </button>

            <!-- Connection Type Filter -->
            <CommentaryConnectionTypeFilter :book="book"
                                            :selected-connection-type-id="selectedConnectionTypeId"
                                            :available-options="availableFilterOptions"
                                            @filter-change="$emit('connection-type-change', $event)" />

            <!-- Category Filter Toggle Button -->
            <div class="category-filter-wrapper"
                 v-if="availableCategories.length > 1">
                <button class="flex-center c-pointer nav-btn"
                        ref="categoryFilterButtonRef"
                        :class="{ 'active': showCategoryDropdown }"
                        @click.stop="toggleCategoryDropdown"
                        :title="selectedCategoryFilter ? `סנן: ${selectedCategoryFilter}` : 'סנן לפי קטגוריה'">
                    <Icon icon="fluent:folder-24-regular"
                          class="small-icon" />
                </button>

                <!-- Category Dropdown -->
                <div v-if="showCategoryDropdown"
                     class="category-dropdown"
                     ref="categoryDropdownRef"
                     @click.stop>
                    <div class="category-option"
                         :class="{ 'selected': selectedCategoryFilter === null }"
                         @click="selectCategory(null)">
                        הכל
                    </div>
                    <div v-for="category in availableCategories"
                         :key="category"
                         class="category-option"
                         :class="{ 'selected': selectedCategoryFilter === category }"
                         @click="selectCategory(category)">
                        {{ category }}
                    </div>
                </div>
            </div>

            <!-- Commentary Selector Combobox -->
            <Combobox :model-value="comboboxSelectedValue"
                      :options="filteredGroupOptions"
                      placeholder="בחר פרשן..."
                      dir="rtl"
                      @update:model-value="$emit('update:combobox-value', $event)" />

            <!-- Search Button -->
            <button class="flex-center c-pointer nav-btn"
                    @click="$emit('open-search')"
                    title="חיפוש (Ctrl+F)">
                <Icon icon="fluent:search-28-regular"
                      class="small-icon" />
            </button>

            <!-- View Mode Toggle Button -->
            <button class="flex-center c-pointer nav-btn"
                    :class="{ 'active': showAllCommentaries }"
                    @click.stop="$emit('toggle-view-mode')"
                    :title="showAllCommentaries ? 'הצג פרשן אחד' : 'הצג את כולם'">
                <Icon icon="fluent:text-bullet-list-ltr-24-regular"
                      class="small-icon"
                      :flip="'horizontal'" />
            </button>

            <!-- Previous/Next Group Buttons -->
            <button class="flex-center c-pointer nav-btn"
                    :disabled="!canNavigateToPreviousGroup"
                    @click.stop="$emit('navigate-previous-group')"
                    title="דלג אחורה">
                <Icon icon="fluent:chevron-up-28-regular"
                      class="small-icon" />
            </button>

            <button class="flex-center c-pointer nav-btn"
                    :disabled="!canNavigateToNextGroup"
                    @click.stop="$emit('navigate-next-group')"
                    title="דלג קדימה">
                <Icon icon="fluent:chevron-down-28-regular"
                      class="small-icon" />
            </button>
        </div>

        <!-- Close Button -->
        <button class="flex-center c-pointer commentary-close-btn"
                @click="$emit('close')"
                title="סגור פאנל">
            <Icon icon="fluent:dismiss-16-regular"
                  class="small-icon" />
        </button>
    </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { Icon } from '@iconify/vue'
import { onClickOutside } from '@vueuse/core'
import Combobox, { type ComboboxOption } from '@/components/shared/Combobox.vue'
import CommentaryConnectionTypeFilter from './CommentaryConnectionTypeFilter.vue'
import type { Book } from '@/data/types/Book'

defineProps<{
    canNavigateToPreviousLine: boolean
    canNavigateToNextLine: boolean
    isNavigatingToLine: boolean
    canNavigateToPreviousGroup: boolean
    canNavigateToNextGroup: boolean
    book?: Book
    selectedConnectionTypeId?: number
    availableFilterOptions: Array<{ label: string; value: number }>
    comboboxSelectedValue: string | number
    filteredGroupOptions: ComboboxOption[]
    showAllCommentaries: boolean
    availableCategories: string[]
    selectedCategoryFilter: string | null
}>()

const emit = defineEmits<{
    'navigate-previous-line': []
    'navigate-next-line': []
    'open-search': []
    'connection-type-change': [id: number]
    'update:combobox-value': [value: string | number]
    'update:category-filter': [value: string | null]
    'navigate-previous-group': []
    'navigate-next-group': []
    'toggle-view-mode': []
    'close': []
}>()

const showCategoryDropdown = ref(false)
const categoryFilterButtonRef = ref<HTMLElement | null>(null)
const categoryDropdownRef = ref<HTMLElement | null>(null)

function toggleCategoryDropdown() {
    showCategoryDropdown.value = !showCategoryDropdown.value
}

function selectCategory(category: string | null) {
    emit('update:category-filter', category)
    showCategoryDropdown.value = false
}

onClickOutside(categoryDropdownRef, (event) => {
    if (categoryFilterButtonRef.value?.contains(event.target as Node)) {
        return
    }
    showCategoryDropdown.value = false
}, { ignore: [categoryFilterButtonRef] })
</script>

<style scoped>
.commentary-header {
    padding: 2px 4px;
    gap: 4px;
}

.commentary-navigation {
    gap: 2px;
}

.nav-btn.active {
    background-color: rgba(128, 128, 128, 0.15);
}

.category-filter-wrapper {
    position: relative;
    display: flex;
    align-items: center;
}

.category-dropdown {
    position: absolute;
    top: 100%;
    left: 0;
    margin-top: 2px;
    background: var(--bg-primary);
    border: 1px solid var(--border-color);
    border-radius: 4px;
    min-width: 120px;
    max-height: 300px;
    overflow-y: auto;
    z-index: 1001;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

.category-option {
    padding: 8px 12px;
    color: var(--text-primary);
    font-size: 12px;
    text-align: right;
    cursor: pointer;
    white-space: nowrap;
    min-height: 32px;
    display: flex;
    align-items: center;
    touch-action: manipulation;
}

@media (hover: none) and (pointer: coarse) {
    .category-option {
        min-height: 36px;
        padding: 10px 14px;
    }
}

.category-option:hover {
    background: var(--hover-bg);
}

.category-option.selected {
    background: var(--accent-color);
    color: white;
}

.category-option.selected:hover {
    opacity: 0.9;
}
</style>
