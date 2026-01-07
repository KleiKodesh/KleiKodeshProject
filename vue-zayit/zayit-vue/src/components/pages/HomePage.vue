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
import { dbManager } from '../../data/dbManager';
import UniformGrid from '../UniformGrid.vue';
import AppTile from '../AppTile.vue';

const tabStore = useTabStore();

const openKezayit = () => {
    tabStore.openKezayitLanding();
};

const openPdf = async () => {
    try {
        console.log('Opening PDF file picker...');
        const result = await dbManager.openPdfFilePicker();
        console.log('PDF picker result:', result);

        if (result.filePath && result.fileName && result.dataUrl) {
            console.log('Converting base64 to blob URL, size:', result.dataUrl.length);

            // Convert base64 string to blob URL for PDF.js
            const binaryString = atob(result.dataUrl);
            const bytes = new Uint8Array(binaryString.length);
            for (let i = 0; i < binaryString.length; i++) {
                bytes[i] = binaryString.charCodeAt(i);
            }
            const blob = new Blob([bytes], { type: 'application/pdf' });
            const blobUrl = URL.createObjectURL(blob);

            console.log('Created blob URL:', blobUrl);

            // C# file picker succeeded - create tab with both file path (persistence) and blob URL (viewing)
            tabStore.openPdfWithFilePathAndBlobUrl(result.fileName, result.filePath, blobUrl);
        } else {
            // Fallback to browser file picker
            const input = document.createElement('input');
            input.type = 'file';
            input.accept = '.pdf';
            input.onchange = (e: Event) => {
                const target = e.target as HTMLInputElement;
                const file = target.files?.[0];
                if (file && file.type === 'application/pdf') {
                    const fileUrl = URL.createObjectURL(file);
                    // Open PDF viewer with blob URL (no persistence across sessions)
                    tabStore.openPdfWithFile(file.name, fileUrl);
                }
            };
            input.click();
        }
    } catch (error) {
        console.error('Failed to open C# file picker, falling back to browser:', error);
        // Fallback to browser file picker
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