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
import UniformGrid from '../UniformGrid.vue';
import AppTile from '../AppTile.vue';

const tabStore = useTabStore();

const openKezayit = () => {
    tabStore.openKezayitLanding();
};

const openPdf = async () => {
    // Always use browser file picker - same method as PDF.js built-in picker
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = '.pdf';
    input.onchange = (e: Event) => {
        const target = e.target as HTMLInputElement;
        const file = target.files?.[0];
        if (file && file.type === 'application/pdf') {
            const fileUrl = URL.createObjectURL(file);  // Blob URL - same as PDF.js!
            console.log('Created blob URL for PDF:', fileUrl);
            // Open PDF viewer with blob URL (efficient streaming, no memory loading)
            tabStore.openPdfWithFile(file.name, fileUrl);
        }
    };
    input.click();
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