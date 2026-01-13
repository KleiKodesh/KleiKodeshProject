<template>
  <div class="pdf-page">
    <!-- Show loading state when recreating virtual URL -->
    <div v-if="isLoadingPdf"
         class="flex-center height-fill">
      <div class="loading-container flex-column">
        <Icon icon="fluent:spinner-ios-20-filled" 
              class="loading-spinner" />
        <span>טוען PDF...</span>
      </div>
    </div>

    <!-- Show placeholder when no PDF source available -->
    <div v-else-if="!hasPdfSource"
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
import { computed, ref } from 'vue';
import { Icon } from '@iconify/vue';
import { useTabStore } from '../../stores/tabStore';
import { pdfService } from '../../services/pdfService';

const tabStore = useTabStore();
const fileInput = ref<HTMLInputElement>();

// Get PDF URL from active tab (blob URL for browser file picker)
const selectedPdfUrl = computed(() => {
  return tabStore.activeTab?.pdfState?.fileUrl || '';
});

// Check if we have any PDF source (file path or blob URL) and not loading
const hasPdfSource = computed(() => {
  const tab = tabStore.activeTab;
  if (!tab?.pdfState) return false;
  
  // Don't show PDF viewer if still loading virtual URL
  if (tab.pdfState.isLoading) return false;
  
  return !!(tab.pdfState.filePath || tab.pdfState.fileUrl);
});

// Show loading state
const isLoadingPdf = computed(() => {
  return tabStore.activeTab?.pdfState?.isLoading || false;
});

// PDF.js viewer URL with file parameter
const pdfViewerUrl = computed(() => {
  const baseUrl = '/pdfjs/web/viewer.html';
  const tab = tabStore.activeTab;

  // Use blob URL (works for both C# and browser)
  const fileSource = selectedPdfUrl.value;

  const finalUrl = fileSource
    ? `${baseUrl}?file=${encodeURIComponent(fileSource)}`
    : baseUrl;
    
  console.log('[PdfViewPage] PDF viewer URL:', finalUrl);
  console.log('[PdfViewPage] File source:', fileSource);
  
  return finalUrl;
});

// Placeholder message based on whether this is a restored tab
const placeholderMessage = computed(() => {
  const tab = tabStore.activeTab;
  if (tab?.pdfState?.fileName) {
    return `בחר שוב את הקובץ: ${tab.pdfState.fileName}`;
  }
  return 'בחר קובץ PDF לצפייה';
});

// Open file picker - use C# PDF service via existing bridge
const openFilePicker = async () => {
  try {
    if (pdfService.isAvailable()) {
      // Use C# PDF service via existing bridge system
      const result = await pdfService.showFilePicker();
      
      if (result.fileName && result.dataUrl) {
        // Update current tab's PDF state with virtual URL
        const tab = tabStore.activeTab;
        if (tab) {
          tab.pdfState = {
            fileName: result.fileName,
            fileUrl: result.dataUrl,  // Virtual HTTPS URL from C#
            filePath: result.fileName  // Store filename for tab restoration reference
          };

          // Update tab title if it's generic
          if (tab.title === 'תצוגת PDF') {
            tab.title = result.fileName;
          }
        }
        
        console.log('[Vue] PDF loaded via C# bridge:', result.fileName, result.dataUrl);
      }
    } else {
      // Fallback to browser file picker if not in WebView2
      fileInput.value?.click();
    }
  } catch (error) {
    console.error('[Vue] Error opening PDF file picker:', error);
    // Fallback to browser file picker on error
    fileInput.value?.click();
  }
};

// Handle file selection - same as PDF.js built-in picker
const handleFileSelect = (event: Event) => {
  const target = event.target as HTMLInputElement;
  const file = target.files?.[0];

  if (file && file.type === 'application/pdf') {
    const fileUrl = URL.createObjectURL(file);  // Blob URL - same as PDF.js!
    const fileName = file.name;

    // Update current tab's PDF state
    const tab = tabStore.activeTab;
    if (tab) {
      tab.pdfState = {
        fileName,
        fileUrl,
        filePath: file.name  // Store filename for tab restoration reference
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

// Note: Blob URLs don't persist across app restarts (same as PDF.js built-in picker)
// User will need to reselect files after restart - this is normal browser behavior

// Note: Blob URLs don't persist across app restarts (same as PDF.js built-in picker)
// User will need to reselect files after restart - this is normal browser behavior
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

.loading-container {
  gap: 16px;
  align-items: center;
  color: var(--text-secondary);
}

.loading-spinner {
  font-size: 32px;
  animation: spin 1s linear infinite;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}
</style>