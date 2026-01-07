<template>
  <div class="pdf-page">
    <!-- Show placeholder when no PDF source available -->
    <div v-if="!hasPdfSource"
         class="flex-center height-fill">
      <input ref="fileInput"
             type="file"
             accept=".pdf"
             @change="handleFileSelect"
             style="display: none">
      <button @click="openFilePicker"
              class="select-pdf-btn flex-row">
        <Icon icon="fluent:document-pdf-28-regular" />
        {{ placeholderMessage }}
      </button>
    </div>

    <!-- Show PDF viewer when URL is available -->
    <iframe v-else
            :src="pdfViewerUrl"
            class="pdf-iframe"
            title="PDF Viewer">
    </iframe>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { Icon } from '@iconify/vue';
import { useTabStore } from '../../stores/tabStore';
import { dbManager } from '../../data/dbManager';

const tabStore = useTabStore();
const fileInput = ref<HTMLInputElement>();

// Get PDF URL from active tab (blob URL for browser file picker)
const selectedPdfUrl = computed(() => {
  return tabStore.activeTab?.pdfState?.fileUrl || '';
});

// Check if we have any PDF source (file path or blob URL)
const hasPdfSource = computed(() => {
  const tab = tabStore.activeTab;
  return !!(tab?.pdfState?.filePath || tab?.pdfState?.fileUrl);
});

// PDF.js viewer URL with file parameter
const pdfViewerUrl = computed(() => {
  const baseUrl = '/pdfjs/web/viewer.html';
  const tab = tabStore.activeTab;

  // Use blob URL (works for both C# and browser)
  const fileSource = selectedPdfUrl.value;

  return fileSource
    ? `${baseUrl}?file=${encodeURIComponent(fileSource)}`
    : baseUrl;
});

// Placeholder message based on whether this is a restored tab
const placeholderMessage = computed(() => {
  const tab = tabStore.activeTab;
  if (tab?.pdfState?.fileName) {
    return `בחר שוב את הקובץ: ${tab.pdfState.fileName}`;
  }
  return 'בחר קובץ PDF לצפייה';
});

// Open file picker - use C# if available, otherwise browser
const openFilePicker = async () => {
  try {
    const result = await dbManager.openPdfFilePicker();

    if (result.filePath && result.fileName) {
      // C# file picker succeeded - use file path for persistence
      const tab = tabStore.activeTab;
      if (tab) {
        tab.pdfState = {
          fileName: result.fileName,
          fileUrl: '', // Not needed when using file path
          filePath: result.filePath
        };

        // Update tab title if it's generic
        if (tab.title === 'תצוגת PDF') {
          tab.title = result.fileName;
        }
      }
    } else {
      // Fallback to browser file picker
      fileInput.value?.click();
    }
  } catch (error) {
    console.error('Failed to open C# file picker, falling back to browser:', error);
    fileInput.value?.click();
  }
};

// Handle file selection
const handleFileSelect = (event: Event) => {
  const target = event.target as HTMLInputElement;
  const file = target.files?.[0];

  if (file && file.type === 'application/pdf') {
    const fileUrl = URL.createObjectURL(file);
    const fileName = file.name;

    // Update current tab's PDF state
    const tab = tabStore.activeTab;
    if (tab) {
      tab.pdfState = {
        fileName,
        fileUrl
      };

      // Update tab title if it's generic
      if (tab.title === 'תצוגת PDF') {
        tab.title = fileName;
      }
    }
  }

  // Clear the input
  target.value = '';
};

// Auto-load PDF from file path when blob URL is missing (after app restart)
watch(() => tabStore.activeTab?.pdfState, async (pdfState) => {
  if (pdfState?.filePath && !pdfState.fileUrl) {
    try {
      console.log('Reloading PDF from file path:', pdfState.filePath);
      const dataUrl = await dbManager.loadPdfFromPath(pdfState.filePath);
      if (dataUrl && tabStore.activeTab?.pdfState) {
        // Convert base64 string to blob URL for PDF.js
        const binaryString = atob(dataUrl);
        const bytes = new Uint8Array(binaryString.length);
        for (let i = 0; i < binaryString.length; i++) {
          bytes[i] = binaryString.charCodeAt(i);
        }
        const blob = new Blob([bytes], { type: 'application/pdf' });
        const blobUrl = URL.createObjectURL(blob);

        console.log('Reloaded PDF as blob URL:', blobUrl);

        // Update the tab's PDF state with the loaded blob URL
        tabStore.activeTab.pdfState.fileUrl = blobUrl;
      }
    } catch (error) {
      console.error('Failed to load PDF from file path:', error);
    }
  }
}, { immediate: true });
</script>

<style scoped>
.pdf-page {
  height: 100%;
  width: 100%;
  display: flex;
  flex-direction: column;
}

.select-pdf-btn {
  border: solid 2px var(--border-color);
  white-space: nowrap;
  min-width: fit-content;
  padding: 20px;
}

.pdf-iframe {
  width: 100%;
  height: 100%;
  border: none;
}
</style>