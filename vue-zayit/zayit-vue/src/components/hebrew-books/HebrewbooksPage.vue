<template>
    <div class="flex-column height-fill">
        <!-- Book list -->
        <div class="flex-110 overflow-y list-container">
            <!-- Loading -->
            <div v-if="isLoading" class="flex-center height-fill">
                <LoadingSpinner text="טוען ספרים..." />
            </div>

            <!-- Error -->
            <div v-else-if="error" class="flex-center height-fill">
                <div class="flex-column flex-center state-message">
                    <span class="state-icon">⚠️</span>
                    <span>שגיאה בטעינת הספרים</span>
                    <span class="text-secondary">{{ error }}</span>
                </div>
            </div>

            <!-- List -->
            <template v-else-if="displayedBooks.length > 0">
                <HebrewbooksListItem
                    v-for="book in displayedBooks"
                    :key="book.id"
                    :book="book"
                    @book-clicked="onBookClicked"
                    @download-clicked="onDownloadClicked"
                />
            </template>

            <!-- Empty -->
            <div v-else class="flex-center height-fill">
                <div class="flex-column flex-center state-message">
                    <span class="state-icon">📚</span>
                    <span v-if="searchTerm">לא נמצאו ספרים</span>
                    <span v-else>אין היסטוריה</span>
                    <span class="text-secondary" v-if="searchTerm">נסה לחפש במילים אחרות</span>
                    <span class="text-secondary" v-else>לחץ על ספרים כדי לראות אותם כאן</span>
                </div>
            </div>
        </div>

        <!-- Search bar -->
        <div class="bar">
            <input
                ref="searchInputRef"
                :value="searchTerm"
                @input="onSearchInput"
                type="text"
                :placeholder="searchPlaceholder"
                :disabled="!isOnline"
                class="width-fill"
                :class="{ 'search-disabled': !isOnline }"
                dir="rtl"
            />
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import LoadingSpinner from '@/components/shared/LoadingSpinner.vue'
import HebrewbooksListItem from '@/components/hebrew-books/HebrewbooksListItem.vue'
import { useHebrewBooks } from '@/components/hebrew-books/useHebrewBooks'
import type { HebrewBook } from '@/data/types/HebrewBook'

const { filteredBooks, isLoading, error, searchTerm, loadBooks, performSearch, trackBookInteraction, openHebrewBookViewer, downloadHebrewBook } = useHebrewBooks()

const searchInputRef = ref<HTMLInputElement>()
const isOnline = ref(navigator.onLine)

const displayedBooks = computed(() => {
    if (isOnline.value || searchTerm.value) return filteredBooks.value
    return filteredBooks.value.slice(0, 10)
})

const searchPlaceholder = computed(() =>
    isOnline.value ? 'חפש ספרים, מחברים או נושאים...' : 'נדרש חיבור לאינטרנט לחיפוש ספר'
)

const onSearchInput = (e: Event) => {
    if (!isOnline.value) return
    performSearch((e.target as HTMLInputElement).value)
}

const onBookClicked = async (book: HebrewBook) => {
    await trackBookInteraction(book.id)
    openHebrewBookViewer(book.id, book.title)
}

const onDownloadClicked = (book: HebrewBook) => {
    downloadHebrewBook(book.id, book.title)
}

const updateOnlineStatus = () => { isOnline.value = navigator.onLine }

onMounted(() => {
    updateOnlineStatus()
    window.addEventListener('online', updateOnlineStatus)
    window.addEventListener('offline', updateOnlineStatus)
    loadBooks()
    searchInputRef.value?.focus()
})

onUnmounted(() => {
    window.removeEventListener('online', updateOnlineStatus)
    window.removeEventListener('offline', updateOnlineStatus)
})
</script>

<style scoped>
.list-container {
    overflow-x: hidden;
}

.state-message {
    gap: 8px;
    text-align: center;
    color: var(--text-secondary);
    font-size: 14px;
}

.state-icon {
    font-size: 40px;
    opacity: 0.6;
    margin-bottom: 4px;
}

.search-disabled {
    opacity: 0.6;
    cursor: not-allowed;
    background-color: var(--bg-secondary);
}
</style>
