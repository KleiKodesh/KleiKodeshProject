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
import { computed, ref } from 'vue';
import { Icon } from '@iconify/vue';
import { useTabStore } from '../../stores/tabStore';

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
  // Always use browser file picker - same method as PDF.js built-in picker
  fileInput.value?.click();
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
</style>