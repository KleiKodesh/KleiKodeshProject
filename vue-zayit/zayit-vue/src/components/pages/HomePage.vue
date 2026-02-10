<template>
    <div class="homepage flex-column height-fill">
        <div class="grid-container">
            <UniformGrid>
                <AppTile label="כזית"
                         image-src="/Kezayit.png"
                         @click="openKezayit" />

                <AppTile :label="searchTileLabel"
                         :icon="searchTileIcon"
                         custom-class="search-tile"
                         :disabled="!isDev && (isSearchIndexing || !isSearchReady)"
                         @click="openKezayitSearch">
                    <template v-if="isSearchIndexing"
                              #icon>
                        <svg class="circular-progress"
                             viewBox="0 0 36 36">
                            <path class="circle-bg"
                                  d="M18 2.0845
                                     a 15.9155 15.9155 0 0 1 0 31.831
                                     a 15.9155 15.9155 0 0 1 0 -31.831" />
                            <path class="circle-progress"
                                  :stroke-dasharray="`${indexingPercentage}, 100`"
                                  d="M18 2.0845
                                     a 15.9155 15.9155 0 0 1 0 31.831
                                     a 15.9155 15.9155 0 0 1 0 -31.831" />
                        </svg>
                    </template>
                </AppTile>

                <AppTile label="ניהול סביבות עבודה"
                         icon="fluent:apps-28-regular"
                         custom-class="workspace-tile"
                         @click="openWorkspaceManager" />

                <AppTile label="PDF"
                         image-src="/pdf.png"
                         @click="openPdf" />

                <AppTile v-if="isOnline"
                         label="היברו-בוקס"
                         image-src="/Hebrewbooks.png"
                         @click="openHebrewBooks" />

                <AppTile label="הגדרות"
                         icon="fluent-color:settings-24"
                         @click="openSettings" />

            </UniformGrid>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useTabStore } from '../../stores/tabStore';
import { pdfService } from '../../services/pdfService';
import { webviewBridge } from '../../services/webviewBridge';
import { bloomSearchService } from '../../services/bloomSearchService';
import UniformGrid from '../UniformGrid.vue';
import AppTile from '../AppTile.vue';

const tabStore = useTabStore();

const isSearchIndexing = ref(false);
const isSearchReady = ref(false);
const indexingPercentage = ref(0);
const isOnline = ref(navigator.onLine);
const isDev = import.meta.env.DEV;
const searchTileLabel = computed(() =>
    isSearchIndexing.value
        ? `יוצר אינדקס ${Math.round(indexingPercentage.value)}%`
        : 'חיפוש כזית'
);
const searchTileIcon = computed(() => isSearchIndexing.value ? '' : 'fluent:search-sparkle-24-filled');

// Check search indexing status on mount
onMounted(async () => {
    if (webviewBridge.isAvailable()) {
        await checkSearchStatus();

        // Poll for indexing progress if indexing or not ready
        if (isSearchIndexing.value || !isSearchReady.value) {
            startProgressPolling();
        }
    }

    // Listen for online/offline events
    window.addEventListener('online', handleOnlineStatus);
    window.addEventListener('offline', handleOnlineStatus);
});

const handleOnlineStatus = () => {
    isOnline.value = navigator.onLine;
};

const checkSearchStatus = async () => {
    try {
        const progress = await bloomSearchService.getIndexingProgress();
        // Handle both PascalCase (from C#) and camelCase
        isSearchIndexing.value = progress.isIndexing ?? (progress as any).IsIndexing ?? false;
        isSearchReady.value = progress.isReady ?? (progress as any).IsReady ?? false;
        indexingPercentage.value = progress.percentage ?? (progress as any).Percentage ?? 0;
        console.log('[HomePage] Search status - Indexing:', isSearchIndexing.value, 'Ready:', isSearchReady.value, 'Percentage:', indexingPercentage.value);
    } catch (error) {
        console.error('[HomePage] Error checking search status:', error);
    }
};

const startProgressPolling = () => {
    const interval = setInterval(async () => {
        await checkSearchStatus();

        // Stop polling when ready and not indexing
        if (isSearchReady.value && !isSearchIndexing.value) {
            clearInterval(interval);
        }
    }, 2000); // Poll every 2 seconds
};

const openKezayit = () => {
    tabStore.openKezayitOpenFilePage();
};

const openPdf = async () => {
    try {
        if (pdfService.isAvailable()) {
            // Use C# PDF service via existing bridge system
            const result = await pdfService.showFilePicker();

            if (result.fileName && result.dataUrl) {
                if (result.originalPath) {
                    // Use method that stores both virtual URL and original path for persistence
                    tabStore.openPdfWithFilePathAndBlobUrl(result.fileName, result.originalPath, result.dataUrl);
                    console.log('[HomePage] PDF loaded via C# bridge with persistence:', result.fileName, result.dataUrl, 'original:', result.originalPath);
                } else {
                    // Fallback to virtual URL only
                    tabStore.openPdfWithFile(result.fileName, result.dataUrl);
                    console.log('[HomePage] PDF loaded via C# bridge:', result.fileName, result.dataUrl);
                }
            }
        } else {
            // Fallback to browser file picker if not in WebView2
            const input = document.createElement('input');
            input.type = 'file';
            input.accept = '.pdf';
            input.onchange = (e: Event) => {
                const target = e.target as HTMLInputElement;
                const file = target.files?.[0];
                if (file && file.type === 'application/pdf') {
                    const fileUrl = URL.createObjectURL(file);  // Blob URL fallback
                    console.log('[HomePage] Created blob URL for PDF:', fileUrl);
                    tabStore.openPdfWithFile(file.name, fileUrl);
                }
            };
            input.click();
        }
    } catch (error) {
        console.error('[HomePage] Error opening PDF file picker:', error);
        // Fallback to browser file picker on error
        const input = document.createElement('input');
        input.type = 'file';
        input.accept = '.pdf';
        input.onchange = (e: Event) => {
            const target = e.target as HTMLInputElement;
            const file = target.files?.[0];
            if (file && file.type === 'application/pdf') {
                const fileUrl = URL.createObjectURL(file);
                tabStore.openPdfWithFile(file.name, fileUrl);
            }
        };
        input.click();
    }
};

const openSettings = () => {
    tabStore.openSettings();
};

const openHebrewBooks = () => {
    tabStore.openHebrewBooks();
};

const openKezayitSearch = () => {
    // Check if search page already exists
    const existingSearchTab = tabStore.tabs.find(t => t.currentPage === 'kezayit-search');
    if (existingSearchTab) {
        // Switch to existing search tab
        tabStore.setActiveTab(existingSearchTab.id);
        return;
    }

    // Create new search tab
    tabStore.openKezayitSearch();
};

const openWorkspaceManager = () => {
    tabStore.openWorkspaceManager();
};
</script>

<style scoped>
.homepage {
    position: relative;
    padding: 2rem;
    align-items: center;
    justify-content: center;
}

/* שכבת שקיפות
.homepage::before {
    content: "";
    position: absolute;
    background: url('/צילום מסך 2026-01-04 233317.png') center / cover no-repeat;
    inset: 0;
    opacity: 0.05;
    z-index: 0;
}

/* כל התוכן מעל הרקע
.homepage>* {
    position: relative;
    z-index: 1;
} */

.grid-container {
    width: 100%;
    max-width: min(90vw, 800px);
    /* Increased to accommodate larger tiles */
    display: flex;
    justify-content: center;
}

/* Search tile warm color styling */
:deep(.search-tile .tile-icon svg) {
    color: #f59e0b;
    filter: drop-shadow(0 0 8px rgba(245, 158, 11, 0.3));
}

:root.dark :deep(.search-tile .tile-icon svg) {
    color: #fbbf24;
    filter: drop-shadow(0 0 8px rgba(251, 191, 36, 0.3));
}

/* Circular progress ring */
.circular-progress {
    width: clamp(1.5rem, 25%, 2.5rem);
    height: clamp(1.5rem, 25%, 2.5rem);
    transform: rotate(-90deg);
}

.circle-bg {
    fill: none;
    stroke: var(--color-border, #e5e7eb);
    stroke-width: 3;
}

.circle-progress {
    fill: none;
    stroke: #f59e0b;
    stroke-width: 3;
    stroke-linecap: round;
    transition: stroke-dasharray 0.3s ease;
}

:root.dark .circle-progress {
    stroke: #fbbf24;
}

:root.dark .circle-bg {
    stroke: #374151;
}

/* Workspace tile purple-blue styling */
:deep(.workspace-tile .tile-icon svg) {
    color: #667eea;
    filter: drop-shadow(0 0 8px rgba(102, 126, 234, 0.3));
}

:root.dark :deep(.workspace-tile .tile-icon svg) {
    color: #818cf8;
    filter: drop-shadow(0 0 8px rgba(129, 140, 248, 0.3));
}
</style>