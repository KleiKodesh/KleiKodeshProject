<template>
    <div class="flex-between bar commentary-header"
         style="position: relative;">
        <!-- Title -->
        <span class="bold smaller-em commentary-title">{{ title }}</span>

        <!-- Navigation Controls -->
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

            <!-- Search Button -->
            <button class="flex-center c-pointer nav-btn"
                    @click="$emit('open-search')"
                    title="חיפוש (Ctrl+F)">
                <Icon icon="fluent:search-28-regular"
                      class="small-icon" />
            </button>

            <!-- Connection Type Filter -->
            <CommentaryConnectionTypeFilter :book="book"
                                            :selected-connection-type-id="selectedConnectionTypeId"
                                            :available-options="availableFilterOptions"
                                            @filter-change="$emit('connection-type-change', $event)" />

            <!-- View Mode Toggle Button -->
            <button class="flex-center c-pointer nav-btn"
                    :class="{ 'active': showAllCommentaries }"
                    @click.stop="$emit('toggle-view-mode')"
                    :title="showAllCommentaries ? 'הצג פרשן אחד' : 'הצג את כולם'">
                <Icon icon="fluent:text-bullet-list-ltr-24-regular"
                      class="small-icon"
                      :flip="'horizontal'" />
            </button>

            <!-- Commentary Selector Combobox -->
            <Combobox :model-value="comboboxSelectedValue"
                      :options="filteredGroupOptions"
                      placeholder="בחר פרשן..."
                      dir="rtl"
                      @update:model-value="$emit('update:combobox-value', $event)" />

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
import { Icon } from '@iconify/vue'
import Combobox, { type ComboboxOption } from './common/Combobox.vue'
import CommentaryConnectionTypeFilter from './CommentaryConnectionTypeFilter.vue'
import type { Book } from '../types/Book'

defineProps<{
    title: string
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
}>()

defineEmits<{
    'navigate-previous-line': []
    'navigate-next-line': []
    'open-search': []
    'connection-type-change': [id: number]
    'update:combobox-value': [value: string | number]
    'navigate-previous-group': []
    'navigate-next-group': []
    'toggle-view-mode': []
    'close': []
}>()
</script>

<style scoped>
.nav-btn.active {
    background-color: rgba(128, 128, 128, 0.15);
}
</style>
