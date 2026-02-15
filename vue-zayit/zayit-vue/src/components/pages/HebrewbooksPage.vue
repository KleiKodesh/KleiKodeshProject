<template>
    <div class="flex-column height-fill">
        <!-- Book List -->
        <div class="flex-110 overflow-y"
             style="overflow-x: hidden;">
            <!-- Loading state -->
            <div v-if="store.isLoading"
                 class="flex-center height-fill">
                <div class="loading-container">
                    <Icon icon="fluent:spinner-ios-20-regular"
                          class="loading-spinner" />
                    <div class="loading-text">טוען ספרים...</div>
                </div>
            </div>

            <!-- Error state -->
            <div v-else-if="store.error"
                 class="flex-center height-fill">
                <div class="flex-column flex-center">
                    <div class="error-icon">⚠️</div>
                    <div class="error-text">שגיאה בטעינת הספרים</div>
                    <div class="error-subtext text-secondary">{{ store.error }}
                    </div>
                </div>
            </div>

            <!-- Book list with virtualization -->
            <template v-else>
                <DynamicScroller v-if="displayedBooks.length > 0"
                                 ref="bookScroller"
                                 :items="displayedBooks"
                                 :min-item-size="80"
                                 :buffer="200"
                                 key-field="id"
                                 class="scroller">
                    <template #default="{ item, index, active }">
                        <DynamicScrollerItem :item="item"
                                             :active="active"
                                             :data-index="index">
                            <HebrewbooksListItem :book="item"
                                                 @book-clicked="trackBookInteraction" />
                        </DynamicScrollerItem>
                    </template>
                </DynamicScroller>

                <!-- Empty state -->
                <div v-else
                     class="flex-center height-fill">
                    <div class="flex-column flex-center">
                        <div class="empty-icon">📚</div>
                        <div v-if="store.searchTerm"
                             class="empty-text">לא נמצאו ספרים
                        </div>
                        <div v-else
                             class="empty-text">אין היסטוריה</div>
                        <div v-if="store.searchTerm"
                             class="empty-subtext text-secondary">נסה
                            לחפש
                            במילים אחרות</div>
                        <div v-else
                             class="empty-subtext text-secondary">לחץ על ספרים
                            כדי לראות
                            אותם כאן</div>
                    </div>
                </div>
            </template>
        </div>

        <!-- Search at bottom -->
        <div class="bar">
            <input ref="searchInput"
                   :value="store.searchTerm"
                   @input="handleSearchInput"
                   @keydown.tab="handleSearchTabKey"
                   @keydown.arrow-down="handleSearchTabKey"
                   @keydown.arrow-up="handleSearchTabKey"
                   type="text"
                   :placeholder="searchPlaceholder"
                   :disabled="!isOnline"
                   class="width-fill search-input"
                   :class="{ 'search-disabled': !isOnline }" />
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, nextTick } from 'vue'
import { Icon } from '@iconify/vue'
import { DynamicScroller, DynamicScrollerItem } from 'vue-virtual-scroller'
import HebrewbooksListItem from '../HebrewbooksListItem.vue'
import type { HebrewBook } from '../../types/HebrewBook'
import { useHebrewBooksStore } from '../../stores/hebrewBooksStore'
import { useListKeyboardNavigation } from '../../composables/useListKeyboardNavigation'

// Use Pinia store
const store = useHebrewBooksStore()

// Online status
const isOnline = ref(navigator.onLine)

// Search input ref
const searchInput = ref<HTMLInputElement>()

// Book scroller ref
const bookScroller = ref<InstanceType<typeof DynamicScroller>>()

// Get the scroller element for keyboard navigation
const scrollerElRef = computed(() => bookScroller.value?.$el as HTMLElement | undefined)

// Set up keyboard navigation for the book list
useListKeyboardNavigation(scrollerElRef, {
    onEscape: () => returnFocusToSearch(),
    onTab: () => returnFocusToSearch()
})

// Return focus to search input
const returnFocusToSearch = () => {
    nextTick(() => {
        searchInput.value?.focus()
    })
}

// Check online status when component mounts or tab becomes active
const checkOnlineStatus = () => {
    isOnline.value = navigator.onLine
}

// Computed books to display - limit to 10 most recent if offline and no search
const displayedBooks = computed(() => {
    if (isOnline.value || store.searchTerm) {
        return store.filteredBooks
    }
    // Offline and no search - show only 10 most recent history items
    return store.filteredBooks.slice(0, 10)
})

// Search placeholder based on online status
const searchPlaceholder = computed(() => {
    return isOnline.value
        ? 'חפש ספרים, מחברים או נושאים...'
        : 'נדרש חיבור לאינטרנט לחיפוש ספר'
})

// Track user interactions
const trackBookInteraction = async (book: HebrewBook) => {
    await store.trackBookInteraction(book.id)
}

// Handle search input changes
const handleSearchInput = (event: Event) => {
    if (!isOnline.value) return
    const target = event.target as HTMLInputElement
    store.performSearch(target.value)
}

// Handle Tab and Arrow keys in search input - move focus to first book item
const handleSearchTabKey = (event: KeyboardEvent) => {
    if (displayedBooks.value.length === 0) return

    event.preventDefault()

    // Find the first book item element and focus it
    const firstBookItem = bookScroller.value?.$el?.querySelector('.tree-node[tabindex="0"]') as HTMLElement
    if (firstBookItem) {
        firstBookItem.focus()
    }
}

// Load books on component mount
onMounted(async () => {
    checkOnlineStatus()
    window.addEventListener('online', checkOnlineStatus)
    window.addEventListener('offline', checkOnlineStatus)

    // Load books (history + start catalog load in background)
    store.loadBooks()

    // Focus search input
    searchInput.value?.focus()
})

onUnmounted(() => {
    window.removeEventListener('online', checkOnlineStatus)
    window.removeEventListener('offline', checkOnlineStatus)
})
</script>

<style scoped>
.scroller {
    height: 100%;
}

.loading-container {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 16px;
    padding: 40px;
    text-align: center;
}

.loading-spinner {
    font-size: 32px;
    color: var(--accent-color);
    animation: spin 1s linear infinite;
}

@keyframes spin {
    from {
        transform: rotate(0deg);
    }

    to {
        transform: rotate(360deg);
    }
}

.error-icon,
.empty-icon {
    font-size: 48px;
    margin-bottom: 16px;
    opacity: 0.6;
}

.loading-text,
.error-text,
.empty-text {
    font-size: 16px;
    font-weight: 500;
    color: var(--text-secondary);
}

.error-subtext,
.empty-subtext {
    font-size: 14px;
}

.search-input {
    padding: 10px 16px;
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

.search-disabled {
    opacity: 0.6;
    cursor: not-allowed;
    background-color: var(--bg-secondary);
}
</style>