<template>
    <div class="flex-column height-fill">
        <!-- Book List -->
        <div class="flex-110 overflow-y"
             style="overflow-x: hidden;">
            <!-- Loading state -->
            <div v-if="isLoading"
                 class="flex-center height-fill">
                <LoadingSpinner text="טוען ספרים..." />
            </div>

            <!-- Error state -->
            <div v-else-if="error"
                 class="flex-center height-fill">
                <div class="flex-column flex-center">
                    <div class="error-icon">⚠️</div>
                    <div class="error-text">שגיאה בטעינת הספרים</div>
                    <div class="error-subtext text-secondary">{{ error }}
                    </div>
                </div>
            </div>

            <!-- Book list with Virtua virtualization -->
            <template v-else>
                <VList v-if="displayedBooks.length > 0"
                       ref="vListRef"
                       :data="displayedBooks"
                       class="scroll-container"
                       style="height: 100%; overflow-y: auto;"
                       tabindex="0">
                    <template #default="{ item }">
                        <div class="book-item">
                            <HebrewBooksListItem :book="item"
                                                 @book-clicked="trackBookInteractionHandler" />
                        </div>
                    </template>
                </VList>

                <!-- Empty state -->
                <div v-else
                     class="flex-center height-fill">
                    <div class="flex-column flex-center">
                        <div class="empty-icon">📚</div>
                        <div v-if="searchTerm"
                             class="empty-text">לא נמצאו ספרים
                        </div>
                        <div v-else
                             class="empty-text">אין היסטוריה</div>
                        <div v-if="searchTerm"
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
                   :value="searchTerm"
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
import { VList } from 'virtua/vue'
import { Icon } from '@iconify/vue'
import LoadingSpinner from '@/components/shared/LoadingSpinner.vue'
import HebrewBooksListItem from '@/components/hebrew-books/HebrewBooksListItem.vue'
import type { HebrewBook } from '@/data/types/HebrewBook'
import { useHebrewBooks } from '@/components/hebrew-books/useHebrewBooks'
import { useListKeyboardNavigation } from '@/components/shared/useListKeyboardNavigation'

// Use composable
const { filteredBooks, isLoading, error, searchTerm, performSearch, trackBookInteraction } = useHebrewBooks()

// Online status
const isOnline = ref(navigator.onLine)

// Search input ref
const searchInput = ref<HTMLInputElement>()

// VList ref for Virtua
const vListRef = ref<InstanceType<typeof VList> | null>(null)

// Get scroll container from VList
const bookScrollContainer = computed(() => {
    return vListRef.value?.$el as HTMLElement | undefined
})

// Set up keyboard navigation for the book list
useListKeyboardNavigation(bookScrollContainer, {
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
    if (isOnline.value || searchTerm.value) {
        return filteredBooks.value
    }
    // Offline and no search - show only 10 most recent history items
    return filteredBooks.value.slice(0, 10)
})

// Search placeholder based on online status
const searchPlaceholder = computed(() => {
    return isOnline.value
        ? 'חפש ספרים, מחברים או נושאים...'
        : 'נדרש חיבור לאינטרנט לחיפוש ספר'
})

// Track user interactions
const trackBookInteractionHandler = async (book: HebrewBook) => {
    await trackBookInteraction(book.id)
}

// Handle search input changes
const handleSearchInput = (event: Event) => {
    if (!isOnline.value) return
    const target = event.target as HTMLInputElement
    performSearch(target.value)
}

// Handle Tab and Arrow keys in search input - move focus to first book item
const handleSearchTabKey = (event: KeyboardEvent) => {
    if (displayedBooks.value.length === 0) return

    event.preventDefault()

    // Find the first book item element and focus it
    const scrollContainer = vListRef.value?.$el as HTMLElement
    if (scrollContainer) {
        const firstBookItem = scrollContainer.querySelector('.tree-node[tabindex="0"]') as HTMLElement
        if (firstBookItem) {
            firstBookItem.focus()
        }
    }
}

// Load books on component mount
onMounted(async () => {
    checkOnlineStatus()
    window.addEventListener('online', checkOnlineStatus)
    window.addEventListener('offline', checkOnlineStatus)

    // Load books (history + start catalog load in background)
    const { loadBooks } = useHebrewBooks()
    loadBooks()

    // Focus search input
    searchInput.value?.focus()
})

onUnmounted(() => {
    window.removeEventListener('online', checkOnlineStatus)
    window.removeEventListener('offline', checkOnlineStatus)
})
</script>

<style scoped>
.scroll-container {
    height: 100%;
    overflow-y: auto;
    overflow-x: hidden;
    outline: none;
}

.error-icon,
.empty-icon {
    font-size: 48px;
    margin-bottom: 16px;
    opacity: 0.6;
}

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