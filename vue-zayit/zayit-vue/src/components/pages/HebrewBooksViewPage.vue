<template>
  <div class="hebrew-books-page">
    <!-- Show loading state while preparing Hebrew book -->
    <div v-if="isLoading"
         class="flex-center height-fill">
      <div class="loading-container">
        <Icon icon="fluent:spinner-ios-20-regular"
              class="loading-spinner" />
      </div>
    </div>

    <!-- Show Hebrew book viewer when ready -->
    <iframe v-else-if="hebrewBookUrl"
            :src="hebrewBookUrl"
            class="hebrew-book-iframe"
            title="Hebrew Book Viewer">
    </iframe>

    <!-- Show error state if something went wrong -->
    <div v-else-if="hasError"
         class="flex-center height-fill">
      <div class="error-container">
        <Icon icon="fluent:error-circle-20-regular"
              class="error-icon" />
        <div class="error-text">שגיאה בטעינת הספר העברי</div>
        <button @click="retryLoad"
                class="retry-btn">
          נסה שוב
        </button>
      </div>
    </div>

    <!-- Show placeholder when not on Hebrew books page or when there's no activity -->
    <div v-else-if="showPlaceholder"
         class="flex-center height-fill">
      <div class="placeholder-container">
        <Icon icon="fluent:book-20-regular"
              class="placeholder-icon" />
        <div class="placeholder-text">בחר ספר עברי לצפייה</div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { Icon } from '@iconify/vue';
import { useTabStore } from '../../stores/tabStore';

const tabStore = useTabStore();
const isLoading = ref(false);
const hasError = ref(false);

// Get Hebrew book URL from active tab
const hebrewBookUrl = computed(() => {
  const tab = tabStore.activeTab;
  if (tab?.pdfState?.source === 'hebrewbook' && tab.pdfState.fileUrl) {
    return `/pdfjs/web/viewer.html?file=${encodeURIComponent(tab.pdfState.fileUrl)}`;
  }
  return '';
});

// Check if we have a Hebrew book source
const hasHebrewBookSource = computed(() => {
  const tab = tabStore.activeTab;
  return !!(tab?.pdfState?.source === 'hebrewbook' && tab.pdfState.fileUrl);
});

// Show placeholder only when we're not on Hebrew books view page or when there's an error
const showPlaceholder = computed(() => {
  const tab = tabStore.activeTab;
  return tab?.currentPage !== 'hebrewbooks-view' && !isLoading.value && !hasError.value && !hasHebrewBookSource.value;
});

// Watch for changes in tab state to manage loading states and auto-reload Hebrew books
watch(() => tabStore.activeTab?.pdfState, async (pdfState, oldPdfState) => {
  if (pdfState?.source === 'hebrewbook') {
    if (pdfState.fileUrl) {
      // Hebrew book is ready
      isLoading.value = false;
      hasError.value = false;
    } else if (pdfState.bookId && pdfState.bookTitle && !isLoading.value) {
      // Hebrew book needs to be reloaded from session (blob URL was cleared)
      // Only reload if we're not already loading and this is a different state
      const shouldReload = !oldPdfState ||
        oldPdfState.bookId !== pdfState.bookId ||
        oldPdfState.source !== 'hebrewbook' ||
        (oldPdfState.fileUrl && !pdfState.fileUrl);

      if (shouldReload) {
        console.log('[HebrewBooksViewPage] Auto-reloading Hebrew book from session:', pdfState.bookId, pdfState.bookTitle);
        isLoading.value = true;
        hasError.value = false;

        try {
          // Import hebrewBooksStore dynamically to avoid circular dependencies
          const { useHebrewBooksStore } = await import('../../stores/hebrewBooksStore');
          const hebrewBooksStore = useHebrewBooksStore();

          // Reload the Hebrew book using the stored bookId and title
          await hebrewBooksStore.openHebrewBookViewer(pdfState.bookId, pdfState.bookTitle);
        } catch (error) {
          console.error('[HebrewBooksViewPage] Failed to auto-reload Hebrew book:', error);
          hasError.value = true;
          isLoading.value = false;
        }
      }
    } else {
      // Hebrew book is being prepared
      isLoading.value = true;
      hasError.value = false;
    }
  } else {
    // No Hebrew book or different source - check if we're on hebrewbooks-view page
    const tab = tabStore.activeTab;
    if (tab?.currentPage === 'hebrewbooks-view') {
      // We're on Hebrew books page but no Hebrew book is being loaded
      isLoading.value = true;
      hasError.value = false;
    } else {
      isLoading.value = false;
      hasError.value = false;
    }
  }
}, { immediate: true, deep: true });

// Also watch for page changes to show loading when navigating to Hebrew books view
watch(() => tabStore.activeTab?.currentPage, (currentPage) => {
  if (currentPage === 'hebrewbooks-view') {
    const tab = tabStore.activeTab;
    if (!tab?.pdfState?.fileUrl) {
      // Just navigated to Hebrew books view but no book is ready
      isLoading.value = true;
      hasError.value = false;
    }
  }
}, { immediate: true });

// Retry loading the Hebrew book
const retryLoad = async () => {
  hasError.value = false;
  isLoading.value = true;

  const tab = tabStore.activeTab;
  const pdfState = tab?.pdfState;

  // If we have bookId and bookTitle, try to reload the Hebrew book
  if (pdfState?.source === 'hebrewbook' && pdfState.bookId && pdfState.bookTitle) {
    try {
      console.log('[HebrewBooksViewPage] Retrying Hebrew book load:', pdfState.bookId, pdfState.bookTitle);

      // Import hebrewBooksStore dynamically to avoid circular dependencies
      const { useHebrewBooksStore } = await import('../../stores/hebrewBooksStore');
      const hebrewBooksStore = useHebrewBooksStore();

      // Reload the Hebrew book using the stored bookId and title
      await hebrewBooksStore.openHebrewBookViewer(pdfState.bookId, pdfState.bookTitle);
    } catch (error) {
      console.error('[HebrewBooksViewPage] Failed to retry Hebrew book load:', error);
      hasError.value = true;
      isLoading.value = false;
    }
  } else {
    // Fallback: just reset the error state
    setTimeout(() => {
      if (!hasHebrewBookSource.value) {
        hasError.value = true;
        isLoading.value = false;
      }
    }, 2000);
  }
};

// Handle error cases (could be expanded)
watch(hebrewBookUrl, (newUrl) => {
  if (!newUrl && !isLoading.value) {
    // If we expected a URL but don't have one, show error
    const tab = tabStore.activeTab;
    if (tab?.pdfState?.source === 'hebrewbook') {
      hasError.value = true;
    }
  }
});
</script>

<style scoped>
.hebrew-books-page {
  height: 100%;
  width: 100%;
  display: flex;
  flex-direction: column;
}

.loading-container,
.error-container,
.placeholder-container {
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

.loading-text {
  font-size: 16px;
  color: var(--text-secondary);
  font-weight: 500;
}

.error-icon {
  font-size: 32px;
  color: var(--error-color, #e74c3c);
}

.error-text {
  font-size: 16px;
  color: var(--text-primary);
  font-weight: 500;
}

.retry-btn {
  padding: 12px 24px;
  border: solid 2px var(--border-color);
  border-radius: 4px;
  background: var(--background-color);
  color: var(--text-primary);
  font-weight: 500;
  cursor: pointer;
  transition: all 0.15s ease;
}

.retry-btn:hover {
  background: var(--hover-bg);
  border-color: var(--accent-color);
}

.placeholder-icon {
  font-size: 48px;
  color: var(--text-secondary);
  opacity: 0.5;
}

.placeholder-text {
  font-size: 18px;
  color: var(--text-secondary);
  font-weight: 500;
}

.hebrew-book-iframe {
  width: 100%;
  height: 100%;
  border: none;
}
</style>