<template>
    <div class="homepage flex-column height-fill">
        <div class="grid">
            <AppTile v-if="isDatabaseAvailable || isDev"
                     label="פתח ספר"
                     icon="fluent:library-28-regular"
                     custom-class="kezayit-tile"
                     @click="openKezayit" />

            <AppTile v-if="isDatabaseAvailable || isDev"
                     :label="searchTileLabel"
                     :icon="searchTileIcon"
                     custom-class="search-tile"
                     :disabled="!isDev && (isSearchIndexing || !isSearchReady)"
                     @click="openZayitSearchPage">
                <template v-if="isSearchIndexing"
                          #icon>
                    <CircularProgress :percentage="indexingPercentage" />
                </template>
            </AppTile>

            <AppTile v-if="!isDatabaseAvailable && !isDev"
                     label="התקן את הספרייה"
                     icon="fluent:library-28-regular"
                     custom-class="warning-tile kezayit-tile"
                     @click="downloadZayit" />

            <AppTile v-if="!isDatabaseAvailable && !isDev"
                     label="בחר קובץ מסד נתונים"
                     icon="fluent:database-24-regular"
                     custom-class="db-select-tile warning-tile"
                     @click="selectDatabaseFile" />

            <AppTile label="ניהול סביבות עבודה"
                     icon="fluent:apps-28-regular"
                     custom-class="workspace-tile"
                     @click="openWorkspaceManagerPage" />

            <AppTile label="מסמך מהמחשב"
                     image-src="/Directory.png"
                     @click="openPdf" />

            <AppTile label="היברו-בוקס"
                     image-src="/Hebrewbooks.png"
                     @click="openHebrewBooksPage" />

            <AppTile label="הגדרות"
                     icon="fluent-color:settings-24"
                     @click="openSettingsPage" />

        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useHome } from '@/components/home/useHome';
import { pdfService } from '@/data/services/pdfService';
import { webviewBridge } from '@/data/services/webviewBridge';
import { bloomSearchService } from '@/data/services/bloomSearchService';
import AppTile from '@/components/shared/AppTile.vue';
import CircularProgress from '@/components/shared/CircularProgress.vue';

const {
    openZayitOpenFilePage,
    openSettings,
    openHebrewBooks,
    openKezayitSearch,
    openWorkspaceManager,
    openPdfWithFile,
    openPdfWithFilePathAndBlobUrl
} = useHome();

const isSearchIndexing = ref(false);
const isSearchReady = ref(false);
const indexingPercentage = ref(0);
const isDatabaseAvailable = ref(true);
const isDev = import.meta.env.DEV;
const searchTileLabel = computed(() =>
    isSearchIndexing.value
        ? `יוצר אינדקס ${Math.round(indexingPercentage.value)}%`
        : 'חיפוש בספרייה'
);
const searchTileIcon = computed(() => isSearchIndexing.value ? '' : 'fluent:search-sparkle-24-filled');

// Check search indexing status on mount
onMounted(async () => {
    // Check database availability (works in both dev and production)
    isDatabaseAvailable.value = await webviewBridge.isDatabaseAvailable();
    console.log('[HomePage] Database available:', isDatabaseAvailable.value);

    if (webviewBridge.isAvailable() && isDatabaseAvailable.value) {
        await checkSearchStatus();

        // Poll for indexing progress if indexing or not ready
        if (isSearchIndexing.value || !isSearchReady.value) {
            startProgressPolling();
        }
    }
});

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
    openZayitOpenFilePage();
};

const openPdf = async () => {
    try {
        if (pdfService.isAvailable()) {
            // Use C# PDF service via existing bridge system
            const result = await pdfService.showFilePicker();

            if (result.fileName && result.dataUrl) {
                if (result.originalPath) {
                    // Use method that stores both virtual URL and original path for persistence
                    openPdfWithFilePathAndBlobUrl(result.fileName, result.originalPath, result.dataUrl);
                    console.log('[HomePage] PDF loaded via C# bridge with persistence:', result.fileName, result.dataUrl, 'original:', result.originalPath);
                } else {
                    // Fallback to virtual URL only
                    openPdfWithFile(result.fileName, result.dataUrl);
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
                    const fileUrl = URL.createObjectURL(file);  // Blob URL - memory efficient
                    console.log('[HomePage] Created blob URL for PDF:', fileUrl);
                    openPdfWithFile(file.name, fileUrl);
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
                openPdfWithFile(file.name, fileUrl);
            }
        };
        input.click();
    }
};

const openSettingsPage = () => {
    openSettings();
};

const openHebrewBooksPage = () => {
    openHebrewBooks();
};

const openZayitSearchPage = () => {
    // Always create new search tab (allow multiple search pages)
    openKezayitSearch();
};

const openWorkspaceManagerPage = () => {
    openWorkspaceManager();
};

const selectDatabaseFile = async () => {
    // In dev mode without C# bridge, show a message
    if (!webviewBridge.isAvailable()) {
        console.log('[HomePage] Database file selection not available in dev mode');
        alert('בחירת קובץ מסד נתונים זמינה רק במצב ייצור עם C#');
        return;
    }

    try {
        const result = await webviewBridge.openDatabaseFilePicker();
        if (result.filePath) {
            const isValid = await webviewBridge.validateDatabasePath(result.filePath);
            if (!isValid) {
                console.error('[HomePage] Invalid database file selected');
                return;
            }

            const success = await webviewBridge.setDatabasePath(result.filePath);
            if (success) {
                await webviewBridge.reloadPage();
            }
        }
    } catch (error) {
        console.error('[HomePage] Error selecting database file:', error);
    }
};

const downloadZayit = async () => {
    try {
        // This works in both dev and production modes
        await webviewBridge.openUrlInBrowser('https://zayitapp.com/#/download');
    } catch (error) {
        console.error('[HomePage] Error opening download page:', error);
    }
};
</script>

<style scoped>
.homepage {
    position: relative;
    padding: 1rem;
    align-items: center;
    justify-content: center;
    overflow-x: hidden;
}

.grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, 90px);
    gap: 12px;
    width: 100%;
    max-width: 800px;
    margin: 0 auto;
    padding: 0.5rem;
    box-sizing: border-box;
    justify-content: center;
}

.grid :deep(.app-tile) {
    width: 90px;
    height: 90px;
}

@media (min-width: 640px) {
    .homepage {
        padding: 2rem;
    }

    .grid {
        padding: 2rem;
    }
}

/* Search tile warm color styling */
:deep(.search-tile .tile-icon .circular-progress) {
    color: #f59e0b;
}

:root.dark :deep(.search-tile .tile-icon .circular-progress) {
    color: #fbbf24;
}

:deep(.search-tile .tile-icon svg) {
    color: #f59e0b;
    filter: drop-shadow(0 0 8px rgba(245, 158, 11, 0.3));
}

:root.dark :deep(.search-tile .tile-icon svg) {
    color: #fbbf24;
    filter: drop-shadow(0 0 8px rgba(251, 191, 36, 0.3));
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

/* Database select tile red styling */
:deep(.db-select-tile .tile-icon svg) {
    color: #ef4444;
    filter: drop-shadow(0 0 8px rgba(239, 68, 68, 0.3));
}

:root.dark :deep(.db-select-tile .tile-icon svg) {
    color: #f87171;
    filter: drop-shadow(0 0 8px rgba(248, 113, 113, 0.3));
}

/* Kezayit tile olive green with golden orange styling */
:deep(.kezayit-tile .tile-icon svg) {
    color: #d4a24a;
}

:root.dark :deep(.kezayit-tile .tile-icon svg) {
    color: #e5b55f;
}

/* Warning tiles - subtle background to indicate action needed */
:deep(.warning-tile) {
    background: linear-gradient(135deg, rgba(251, 191, 36, 0.08) 0%, rgba(245, 158, 11, 0.05) 100%);
    border: 2px dashed rgba(251, 191, 36, 0.3);
}

:deep(.warning-tile:hover) {
    background: linear-gradient(135deg, rgba(251, 191, 36, 0.12) 0%, rgba(245, 158, 11, 0.08) 100%);
    border-color: rgba(251, 191, 36, 0.4);
}

:root.dark :deep(.warning-tile) {
    background: linear-gradient(135deg, rgba(251, 191, 36, 0.12) 0%, rgba(245, 158, 11, 0.08) 100%);
    border: 2px dashed rgba(251, 191, 36, 0.35);
}

:root.dark :deep(.warning-tile:hover) {
    background: linear-gradient(135deg, rgba(251, 191, 36, 0.16) 0%, rgba(245, 158, 11, 0.12) 100%);
    border-color: rgba(251, 191, 36, 0.45);
}
</style>