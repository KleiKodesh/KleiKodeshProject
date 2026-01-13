<template>
    <div class="homepage flex-column height-fill">
        <div class="grid-container">
            <UniformGrid>
                <AppTile label="כזית"
                         image-src="/Kezayit.png"
                         @click="openKezayit" />

                <AppTile label="PDF"
                         image-src="/pdf.png"
                         @click="openPdf" />

                <AppTile label="הגדרות"
                         icon="fluent-color:settings-24"
                         @click="openSettings" />

                <AppTile label="היברו-בוקס"
                         image-src="/Hebrewbooks.png"
                         @click="openHebrewBooks" />

                <!-- <AppTile label="חיפוש כזית"
                         icon="fluent-color:search-24"
                         @click="openKezayitSearch" /> -->
            </UniformGrid>
        </div>
    </div>
</template>

<script setup lang="ts">
import { useTabStore } from '../../stores/tabStore';
import { pdfService } from '../../services/pdfService';
import UniformGrid from '../UniformGrid.vue';
import AppTile from '../AppTile.vue';

const tabStore = useTabStore();

const openKezayit = () => {
    tabStore.openKezayitLanding();
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
    tabStore.openKezayitSearch();
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
</style>