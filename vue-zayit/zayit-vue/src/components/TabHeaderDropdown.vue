<template>
  <div class="dropdown-container">
    <button @click.stop="toggleDropdown"
            class="flex-center c-pointer dropdown-toggle"
            title="אפשרויות">
      <Icon icon="fluent:more-vertical-28-regular" />
    </button>

    <transition name="slide">
      <div v-if="isOpen"
           class="dropdown-menu">
        <div class="dropdown-content">
          <!-- Diacritics toggle dropdown item -->
          <DiacriticsDropdown />

          <!-- Alt TOC toggle dropdown item -->
          <div v-if="tabStore.activeTab?.currentPage === 'bookview'"
               @click.stop="handleAltTocToggleClick"
               class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
            <Icon icon="fluent:eye-lines-28-regular"/>
            <span class="dropdown-label">{{ isAltTocVisible ? 'הסתר כותרות נוספות' : 'הצג כותרות נוספות' }}</span>
          </div>

          <!-- Line display toggle dropdown item -->
          <div v-if="tabStore.activeTab?.currentPage === 'bookview'"
               @click.stop="handleLineDisplayClick"
               class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
            <Icon icon="fluent:text-align-right-24-regular"
                  v-if="isLineDisplayInline" />
            <Icon icon="fluent:text-align-justify-24-regular"
                  v-else />
            <span class="dropdown-label">{{ isLineDisplayInline ? 'תצוגת בלוק' : 'תצוגת שורה' }}</span>
          </div>

          <!-- Virtualization toggle dropdown item -->
          <div v-if="tabStore.activeTab?.currentPage === 'bookview'"
               @click.stop="handleVirtualizationClick"
               class="flex-row flex-center-start hover-bg c-pointer dropdown-item"
               :title="settingsStore.enableVirtualization
                ? 'כל השורות נטענות'
                : 'רק חלק מהשורות נטענות'">
            <Icon icon="fluent:flash-28-regular"
                  v-if="settingsStore.enableVirtualization" />
            <Icon icon="fluent:leaf-24-regular"
                  v-else />
            <span class="dropdown-label">{{ settingsStore.enableVirtualization
              ? 'בטל טעינת שורות דינמית' : 'הפעל טעינת שורות דינמית' }}</span>
          </div>

          <div @click.stop="handleThemeClick"
               class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
            <Icon :icon="themeToggleIcon"
                  class="theme-icon" />
            <span class="dropdown-label">{{ themeToggleText }}</span>
          </div>

          <div @click.stop="handleSettingsClick"
               class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
            <Icon icon="fluent:settings-28-regular" />
            <span class="dropdown-label">הגדרות</span>
          </div>

          <div @click.stop="handleAboutClick"
               class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
            <Icon icon="fluent:info-28-regular" />
            <span class="dropdown-label">אודות</span>
          </div>

          <!-- Hebrew Books - only show on Zayit landing page, not homepage -->
          <div v-if="!isHomepage"
               @click.stop="handleHebrewBooksClick"
               class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
            <Icon icon="fluent:library-28-regular" />
            <span class="dropdown-label">היברו-בוקס</span>
          </div>

          <!-- PDF viewer - only show on Zayit landing page, not homepage -->
          <div v-if="!isHomepage"
               @click.stop="handleOpenPdfClick"
               class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
            <Icon icon="fluent:document-pdf-28-regular" />
            <span class="dropdown-label">פתח PDF</span>
          </div>

          <!-- Popout toggle - only available in C# WebView -->
          <div v-if="isWebViewAvailable"
               @click.stop="handlePopoutClick"
               class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
            <Icon icon="fluent:open-28-regular" />
            <span class="dropdown-label">הצג בחלונית</span>
          </div>
        </div>
      </div>
    </transition>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, onMounted, onUnmounted } from 'vue';
import { Icon } from '@iconify/vue';
import DiacriticsDropdown from './TabHeaderDiacriticsDropdown.vue';
import { useTabStore } from '../stores/tabStore';
import { useSettingsStore } from '../stores/settingsStore';
import { toggleTheme, isDarkTheme, syncPdfViewerTheme } from '../utils/theme';
import { pdfService } from '../services/pdfService';

const tabStore = useTabStore();
const settingsStore = useSettingsStore();
const isOpen = ref(false);

// Theme state - reactive to theme changes
const currentTheme = ref(isDarkTheme());

const emit = defineEmits<{
  'close': []
}>();

// Check if WebView is available for popout functionality
const isWebViewAvailable = computed(() => {
  return (window as any).chrome?.webview?.postMessage !== undefined;
});

// Check if current page is homepage (not Zayit landing page)
const isHomepage = computed(() => {
  return tabStore.activeTab?.currentPage === 'homepage';
});

// Line display state
const isLineDisplayInline = computed(() => {
  return tabStore.activeTab?.bookState?.isLineDisplayInline || false;
});

// Alt TOC visibility state
const isAltTocVisible = computed(() => {
  const bookState = tabStore.activeTab?.bookState;
  if (!bookState) return true; // Default to visible
  return bookState.showAltToc !== false; // Default to true if undefined
});

// Theme toggle computed properties
const themeToggleText = computed(() => {
  return currentTheme.value ? 'מצב בהיר' : 'מצב כהה';
});

const themeToggleIcon = computed(() => {
  return currentTheme.value ? 'fluent:weather-sunny-24-regular' : 'fluent:dark-theme-24-regular';
});

const toggleDropdown = () => {
  isOpen.value = !isOpen.value;
};

const closeDropdown = () => {
  isOpen.value = false;
  emit('close');
};

const handleSettingsClick = () => {
  tabStore.openSettings();
  closeDropdown();
};

const handleAboutClick = () => {
  tabStore.openAbout();
  closeDropdown();
};

const handleHebrewBooksClick = () => {
  tabStore.openHebrewBooks();
  closeDropdown();
};

const handleThemeClick = () => {
  toggleTheme();
  // Update reactive state after theme toggle
  currentTheme.value = isDarkTheme();
  
  // Sync theme with any open PDF.js viewers
  // Small delay to ensure theme classes are applied first
  setTimeout(() => {
    syncPdfViewerTheme();
  }, 50);
};

const handleLineDisplayClick = () => {
  tabStore.toggleLineDisplay();
};

const handleVirtualizationClick = () => {
  settingsStore.enableVirtualization = !settingsStore.enableVirtualization;
  closeDropdown();
};

const handleAltTocToggleClick = () => {
  tabStore.toggleAltTocDisplay();
  closeDropdown();
};

const handleOpenPdfClick = async () => {
  console.log('[TabHeaderDropdown] PDF button clicked - starting file picker');
  
  try {
    if (pdfService.isAvailable()) {
      console.log('[TabHeaderDropdown] C# PDF service available, using bridge');
      // Use C# PDF service via existing bridge system
      const result = await pdfService.showFilePicker();
      
      console.log('[TabHeaderDropdown] PDF service result:', result);
      
      if (result.fileName && result.dataUrl) {
        if (result.originalPath) {
          // Use method that stores both virtual URL and original path for persistence
          tabStore.openPdfWithFilePathAndBlobUrl(result.fileName, result.originalPath, result.dataUrl);
          console.log('[TabHeaderDropdown] PDF loaded via C# bridge with persistence:', result.fileName, result.dataUrl, 'original:', result.originalPath);
        } else {
          // Fallback to virtual URL only
          tabStore.openPdfWithFile(result.fileName, result.dataUrl);
          console.log('[TabHeaderDropdown] PDF loaded via C# bridge:', result.fileName, result.dataUrl);
        }
      }
    } else {
      console.log('[TabHeaderDropdown] C# PDF service not available, using browser fallback');
      // Fallback to browser file picker if not in WebView2
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
  } catch (error) {
    console.error('[TabHeaderDropdown] Error opening PDF file picker:', error);
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

  closeDropdown();
};

const handlePopoutClick = () => {
  if (isWebViewAvailable.value) {
    (window as any).chrome.webview.postMessage({
      command: 'TogglePopOut',
      args: []
    });
  }
  closeDropdown();
};

const handleClickOutside = (event: MouseEvent) => {
  const target = event.target as HTMLElement;
  if (!target.closest('.dropdown-container')) {
    closeDropdown();
  }
};

const handleWindowBlur = () => {
  if (isOpen.value) {
    closeDropdown();
  }
};

const handleVisibilityChange = () => {
  if (document.hidden && isOpen.value) {
    closeDropdown();
  }
};

onMounted(() => {
  document.addEventListener('click', handleClickOutside);
  window.addEventListener('blur', handleWindowBlur);
  document.addEventListener('visibilitychange', handleVisibilityChange);
});

onUnmounted(() => {
  document.removeEventListener('click', handleClickOutside);
  window.removeEventListener('blur', handleWindowBlur);
  document.removeEventListener('visibilitychange', handleVisibilityChange);
});

// Expose the isOpen state for parent component
defineExpose({
  isOpen
});
</script>

<style scoped>
.dropdown-container {
  position: relative;
  z-index: 9999;
  color: var(--text-primary);
}

.dropdown-menu {
  position: fixed;
  top: 48px;
  right: 0;
  background: var(--bg-secondary);
  border-left: 1px solid var(--border-color);
  border-bottom: 1px solid var(--border-color);
  border-radius: 0 0 0 4px;
  min-width: 200px;
  max-height: calc(100vh - 60px);
  z-index: 9999;
  overflow: hidden;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

.dropdown-content {
  max-height: calc(100vh - 60px);
  overflow-y: auto;
  overflow-x: hidden;
}

/* Custom scrollbar for dropdown */
.dropdown-content::-webkit-scrollbar {
  width: 6px;
}

.dropdown-content::-webkit-scrollbar-track {
  background: transparent;
}

.dropdown-content::-webkit-scrollbar-thumb {
  background: var(--border-color);
  border-radius: 3px;
}

.dropdown-content::-webkit-scrollbar-thumb:hover {
  background: var(--text-secondary);
}

.dropdown-item {
  gap: 12px;
  width: 100%;
  padding: 10px 16px;
  background: transparent;
  border: none;
  text-align: right;
  direction: rtl;
  color: var(--text-primary);
  border-radius: 0;
  transition: background-color 0.15s ease;
  opacity: 0.8;
  flex-shrink: 0;
}

.dropdown-item:hover {
  background: var(--hover-bg);
  opacity: 0.9;
}

.dropdown-item:active {
  background: var(--active-bg);
}

/* Force dropdown icons to match button icons exactly */
.dropdown-item svg,
.dropdown-item .iconify {
  flex-shrink: 0;
  width: 20px;
  height: 20px;
  color: var(--text-primary);
}

/* Apply the exact same styling as button .iconify */
.dropdown-item .iconify svg,
.dropdown-item .iconify svg * {
  fill: currentColor !important;
  stroke: currentColor !important;
}

.dropdown-item .theme-icon {
  width: 18px;
  height: 18px;
}

.dropdown-label {
  font-size: 14px;
  color: var(--text-primary);
  white-space: nowrap;
}

.dropdown-item :deep(.theme-toggle) {
  pointer-events: none;
}

/* Slide transition */
.slide-enter-active,
.slide-leave-active {
  transition: all 0.2s ease;
}

.slide-enter-from {
  opacity: 0;
  transform: translateY(-10px);
}

.slide-leave-to {
  opacity: 0;
  transform: translateY(-10px);
}
</style>