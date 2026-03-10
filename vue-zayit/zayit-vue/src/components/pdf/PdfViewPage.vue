<template>
  <div class="pdf-page">
    <!-- Show loading state when recreating virtual URL -->
    <div v-if="isLoadingPdf"
         class="flex-center height-fill">
      <LoadingSpinner text="טוען PDF..." />
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
            ref="pdfIframe"
            :src="pdfViewerUrl"
            class="pdf-iframe"
            title="PDF Viewer"
            @load="onPdfIframeLoad">
    </iframe>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { Icon } from '@iconify/vue';
import LoadingSpinner from '@/components/shared/LoadingSpinner.vue';
import { usePdf } from '@/components/pdf/usePdf';
import { pdfService } from '@/data/services/pdfService';
import { syncPdfViewerTheme, isDarkTheme } from '@/utils/themes';

const { activeTab } = usePdf();
const fileInput = ref<HTMLInputElement>();
const pdfIframe = ref<HTMLIFrameElement>();

// Get PDF URL from active tab (blob URL for browser file picker)
const selectedPdfUrl = computed(() => {
  return activeTab.value?.pdfState?.fileUrl || '';
});

// Check if we have any PDF source (file path or blob URL) and not loading
const hasPdfSource = computed(() => {
  const tab = activeTab.value;
  if (!tab?.pdfState) return false;

  // Don't show PDF viewer if still loading virtual URL
  if (tab.pdfState.isLoading) return false;

  return !!(tab.pdfState.filePath || tab.pdfState.fileUrl);
});

// Show loading state
const isLoadingPdf = computed(() => {
  return activeTab.value?.pdfState?.isLoading || false;
});

// PDF.js viewer URL with file parameter and Hebrew locale
const pdfViewerUrl = computed(() => {
  const baseUrl = '/pdfjs/web/viewer.html';
  const tab = activeTab.value;

  // Use blob URL (works for both C# and browser)
  const fileSource = selectedPdfUrl.value;

  // Build URL with file parameter and Hebrew locale
  const params = new URLSearchParams();
  if (fileSource) {
    params.set('file', fileSource);
  }

  // Pass filename for proper document properties and save dialog
  if (tab?.pdfState?.fileName) {
    params.set('filename', encodeURIComponent(tab.pdfState.fileName));
  }

  // Force Hebrew locale for tooltips
  params.set('locale', 'he');

  // Performance optimizations for large files
  params.set('disableAutoFetch', 'false'); // Enable auto-fetch for better performance
  params.set('disableStream', 'false'); // Enable streaming for faster loading
  params.set('disableRange', 'false'); // Enable range requests for partial loading
  params.set('enableHWA', 'true'); // Enable hardware acceleration
  params.set('cMapPacked', 'true'); // Use packed CMaps for faster font loading

  const finalUrl = `${baseUrl}?${params.toString()}`;

  console.log('[PdfViewPage] PDF viewer URL:', finalUrl);
  console.log('[PdfViewPage] File source:', fileSource);

  return finalUrl;
});

// Placeholder message based on whether this is a restored tab
const placeholderMessage = computed(() => {
  const tab = activeTab.value;
  if (tab?.pdfState?.fileName) {
    return `בחר שוב את הקובץ: ${tab.pdfState.fileName}`;
  }
  return 'בחר קובץ PDF לצפייה';
});

// Sync theme when PDF iframe loads
const onPdfIframeLoad = () => {
  // Small delay to ensure PDF.js is fully initialized
  setTimeout(() => {
    syncPdfViewerTheme();
  }, 100);
};

// Open file picker - use C# PDF service via existing bridge
const openFilePicker = async () => {
  try {
    if (pdfService.isAvailable()) {
      console.log('[PdfViewPage] Opening PDF file picker (may take several minutes for large files)...');
      // Use C# PDF service via existing bridge system
      const result = await pdfService.showFilePicker();

      if (result.fileName && result.dataUrl) {
        // Update current tab's PDF state with virtual URL
        const tab = activeTab.value;
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

        console.log('[PdfViewPage] PDF loaded via C# bridge:', result.fileName, result.dataUrl);
      } else {
        console.log('[PdfViewPage] PDF file picker cancelled or returned no result');
      }
    } else {
      // Fallback to browser file picker if not in WebView2
      fileInput.value?.click();
    }
  } catch (error) {
    console.error('[PdfViewPage] Error opening PDF file picker:', error);
    
    // Check if it's a timeout error
    if (error instanceof Error && error.message.includes('timeout')) {
      console.error('[PdfViewPage] PDF conversion timed out - file may be too large or conversion failed');
    }
    
    // Fallback to browser file picker on error
    fileInput.value?.click();
  }
};

// Handle file selection - use blob URL for memory efficiency
const handleFileSelect = (event: Event) => {
  const target = event.target as HTMLInputElement;
  const file = target.files?.[0];

  if (file && file.type === 'application/pdf') {
    const fileUrl = URL.createObjectURL(file);  // Blob URL - memory efficient
    const fileName = file.name;

    // Update current tab's PDF state
    const tab = activeTab.value;
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