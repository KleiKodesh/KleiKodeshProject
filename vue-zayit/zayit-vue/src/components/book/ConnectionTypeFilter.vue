<template>
    <div class="connection-type-filter"
         ref="filterRef">
        <button @click="toggleDropdown"
                class="flex-center c-pointer touch-interactive"
                title="סנן לפי סוג קישור">
            <Icon icon="fluent:filter-24-regular" />
        </button>

        <transition name="slide">
            <div v-if="showDropdown"
                 class="connection-type-dropdown">
                <div v-for="type in connectionTypes"
                     :key="type.id"
                     @click="selectType(type.id)"
                     class="connection-type-option"
                     :class="{ active: selectedTypeId === type.id }">
                    {{ type.hebrewLabel }}
                </div>
            </div>
        </transition>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { Icon } from '@iconify/vue'
import { onClickOutside } from '@vueuse/core'
import { useConnectionTypesStore } from '@/data/stores/connectionTypesStore'
import { useTabStore } from '@/data/stores/tabStore'
import type { Book } from '@/data/types/Book'

const props = defineProps<{
    book?: Book
}>()

const connectionTypesStore = useConnectionTypesStore()
const tabStore = useTabStore()

const showDropdown = ref(false)
const filterRef = ref<HTMLElement>()

// Load connection types on mount and set default if not set
onMounted(async () => {
    await connectionTypesStore.loadConnectionTypes()

    // If no connection type is selected, set default
    if (tabStore.activeTab?.bookState && !tabStore.activeTab.bookState.commentaryFilterConnectionTypeId) {
        // Try COMMENTARY (מפרשים) first as default
        const commentaryType = connectionTypes.value.find(t => t.name === 'COMMENTARY')
        let defaultId = commentaryType?.id

        // If COMMENTARY not available, use first available type
        if (!defaultId && connectionTypes.value.length > 0) {
            defaultId = connectionTypes.value[0]?.id
        }

        if (defaultId) {
            tabStore.activeTab.bookState.commentaryFilterConnectionTypeId = defaultId
        }
    }
})

// Close dropdown on click outside
onClickOutside(filterRef, () => {
    showDropdown.value = false
})

const connectionTypes = computed(() => {
    const allTypes = connectionTypesStore.connectionTypesWithLabels

    // If no book, return all types
    if (!props.book) return allTypes

    // Filter based on book's connection flags
    return allTypes.filter(type => {
        switch (type.name) {
            case 'SOURCE':
                return props.book!.hasSourceConnection > 0
            case 'COMMENTARY':
                return props.book!.hasCommentaryConnection > 0
            case 'TARGUM':
                return props.book!.hasTargumConnection > 0
            case 'REFERENCE':
                return props.book!.hasReferenceConnection > 0
            case 'OTHER':
                return props.book!.hasOtherConnection > 0
            default:
                return false
        }
    })
})

const selectedTypeId = computed(() =>
    tabStore.activeTab?.bookState?.commentaryFilterConnectionTypeId
)

function toggleDropdown() {
    showDropdown.value = !showDropdown.value
}

function selectType(typeId: number) {
    if (tabStore.activeTab?.bookState) {
        tabStore.activeTab.bookState.commentaryFilterConnectionTypeId = typeId
    }
    showDropdown.value = false
}
</script>

<style scoped>
.connection-type-filter {
    position: relative;
}

.connection-type-dropdown {
    position: absolute;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 4px;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
    min-width: 150px;
    max-height: 300px;
    overflow-y: auto;
    z-index: 1000;
}

.connection-type-option {
    padding: 8px 12px;
    cursor: pointer;
    transition: background-color 0.15s ease;
    white-space: nowrap;
}

.connection-type-option:hover {
    background: var(--hover-bg);
}

.connection-type-option.active {
    background: var(--active-bg);
    font-weight: bold;
}

.slide-enter-active,
.slide-leave-active {
    transition: opacity 0.15s ease, transform 0.15s ease;
}

.slide-enter-from,
.slide-leave-to {
    opacity: 0;
    transform: translateY(-8px);
}
</style>
