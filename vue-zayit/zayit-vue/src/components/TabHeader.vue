<template>
  <div class="bar c-pointer tab-header">
    <div class="flex-row">
      <!-- Dropdown menu -->
      <div class="dropdown-container">
        <button @click.stop="toggleDropdown"
                class="flex-center c-pointer dropdown-toggle"
                title="אפשרויות">
          <Icon icon="fluent:more-vertical-28-regular" />
        </button>

        <transition name="slide">
          <div v-if="isDropdownOpen"
               class="dropdown-menu">

            <!-- Diacritics toggle dropdown item -->
            <DiacriticsDropdown />

            <!-- Line display toggle dropdown item -->
            <div v-if="tabStore.activeTab?.currentPage === 'bookview'"
                 @click.stop="handleLineDisplayClick"
                 class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
              <Icon icon="fluent:text-align-right-24-regular"
                    v-if="isLineDisplayInline" />
              <Icon icon="fluent:text-align-justify-24-regular"
                    v-else />
              <span class="dropdown-label">{{ isLineDisplayInline ? 'תצוגת בלוק'
                :
                'תצוגת שורה' }}</span>
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

            <!-- Hebrew Books -->
            <div @click.stop="handleHebrewBooksClick"
                 class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
              <Icon icon="fluent:library-28-regular" />
              <span class="dropdown-label">היברו-בוקס</span>
            </div>

            <!-- PDF viewer - available in both dev and C# modes -->
            <div @click.stop="handleOpenPdfClick"
                 class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
              <Icon icon="fluent:document-pdf-28-regular" />
              <span class="dropdown-label">פתח PDF</span>
            </div>

            <!-- Popout toggle - only available in C# WebView -->
            <div v-if="isWebViewAvailable"
                 @click.stop="handlePopoutClick"
                 class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
              <Icon icon="fluent:open-28-regular" />
              <span class="dropdown-label">{{ popoutLabel }}</span>
            </div>
          </div>
        </transition>
      </div>

      <button v-if="tabStore.activeTab?.currentPage === 'bookview'"
              @click.stop="handleButtonClick(goToToc)"
              class="flex-center c-pointer"
              title="תוכן עניינים">
        <Icon icon="fluent:text-bullet-list-tree-24-regular"
              class="rtl-flip" />
      </button>

      <button v-if="tabStore.activeTab?.currentPage === 'bookview' && !isTocVisible"
              @click.stop="handleButtonClick(openSearch)"
              class="flex-center c-pointer"
              title="חיפוש (Ctrl+F)">
        <Icon icon="fluent:search-28-filled" />
      </button>

      <button v-if="tabStore.activeTab?.currentPage === 'bookview' && hasConnections && !isTocVisible"
              @click.stop="handleButtonClick(toggleSplitPane)"
              class="flex-center c-pointer"
              :title="isSplitPaneOpen ? 'הסתר קשרים' : 'הצג קשרים'">
        <Icon
              :icon="isSplitPaneOpen ? 'fluent:panel-bottom-expand-20-filled' : 'fluent:panel-bottom-contract-20-filled'" />
      </button>




    </div>
    <span class="center-text ellipsis activetab-title">{{ tabStore.activeTab?.title
      }}</span>
    <div class="flex-row justify-end">
      <button @click.stop="handleButtonClick(resetTab)"
              class="flex-center c-pointer"
              title="דף הבית">
        <Icon icon="fluent:home-28-regular" />
      </button>

      <button @click.stop="handleButtonClick(newTab)"
              class="flex-center c-pointer"
              title="פתח טאב חדש">
        <Icon icon="fluent:add-16-filled"
              class="small-icon" />
      </button>

      <button @click.stop="handleButtonClick(closeTab)"
              class="flex-center c-pointer"
              title="סגור">
        <Icon icon="fluent:dismiss-16-filled"
              class="small-icon" />
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, onMounted, onUnmounted } from 'vue';
import { Icon } from '@iconify/vue';
import DiacriticsDropdown from './TabHeaderDiacriticsDropdown.vue';
import { useTabStore } from '../stores/tabStore';
import { useSettingsStore } from '../stores/settingsStore';
import { toggleTheme, isDarkTheme } from '../utils/theme';
import { dbManager } from '../data/dbManager';

const tabStore = useTabStore();
const settingsStore = useSettingsStore();
const isDropdownOpen = ref(false);
const isPopoutMode = ref(false);

// Theme state - reactive to theme changes
const currentTheme = ref(isDarkTheme());

const emit = defineEmits<{
  'close-tab-dropdown': []
}>();

// Check if WebView is available for popout functionality
const isWebViewAvailable = computed(() => {
  return (window as any).chrome?.webview?.postMessage !== undefined;
});



// Line display state
const isLineDisplayInline = computed(() => {
  return tabStore.activeTab?.bookState?.isLineDisplayInline || false;
});

const hasConnections = computed(() => {
  const bookState = tabStore.activeTab?.bookState;
  if (!bookState) return false;
  return bookState.hasConnections || false;
});

const isTocVisible = computed(() => {
  const bookState = tabStore.activeTab?.bookState;
  if (!bookState) return false;
  return bookState.isTocOpen || false;
});

const isSplitPaneOpen = computed(() => {
  const bookState = tabStore.activeTab?.bookState;
  if (!bookState) return false;
  return bookState.showBottomPane || false;
});

const popoutLabel = computed(() => {
  return isPopoutMode.value ? 'פאנל צד' : 'חלון נפרד';
});

// Theme toggle computed properties
const themeToggleText = computed(() => {
  return currentTheme.value ? 'מצב בהיר' : 'מצב כהה';
});

const themeToggleIcon = computed(() => {
  return currentTheme.value ? 'fluent:weather-sunny-24-regular' : 'fluent:dark-theme-24-regular';
});

const goToToc = () => {
  const tab = tabStore.activeTab;
  if (tab?.bookState) {
    if (tab.bookState.isTocOpen) {
      tabStore.closeToc();
    } else {
      tabStore.openBookToc(tab.title, tab.bookState.bookId);
    }
  }
};

const openSearch = () => {
  tabStore.toggleBookSearch(true);
};

const toggleSplitPane = () => {
  tabStore.toggleSplitPane();
};

const resetTab = () => {
  tabStore.resetTab();
};

const newTab = () => {
  tabStore.addTab();
};

const closeTab = () => {
  tabStore.closeTab();
};

const toggleDropdown = () => {
  isDropdownOpen.value = !isDropdownOpen.value;
};

const handleButtonClick = (action: () => void) => {
  action();
  emit('close-tab-dropdown');
};

const handleSettingsClick = () => {
  tabStore.openSettings();
  isDropdownOpen.value = false;
};

const handleAboutClick = () => {
  tabStore.openAbout();
  isDropdownOpen.value = false;
};

const handleHebrewBooksClick = () => {
  tabStore.openHebrewBooks();
  isDropdownOpen.value = false;
};

const handleThemeClick = () => {
  toggleTheme();
  // Update reactive state after theme toggle
  currentTheme.value = isDarkTheme();
};



const handleLineDisplayClick = () => {
  tabStore.toggleLineDisplay();
};

const handleVirtualizationClick = () => {
  settingsStore.enableVirtualization = !settingsStore.enableVirtualization;
  isDropdownOpen.value = false;
};

const handleOpenPdfClick = async () => {
  try {
    // Try C# file picker first if WebView is available
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

  isDropdownOpen.value = false;
};

const handlePopoutClick = () => {
  if (isWebViewAvailable.value) {
    (window as any).chrome.webview.postMessage({
      command: 'TogglePopOut',
      args: []
    });
    // Toggle the local state
    isPopoutMode.value = !isPopoutMode.value;
  }
  isDropdownOpen.value = false;
};

const handleClickOutside = (event: MouseEvent) => {
  const target = event.target as HTMLElement;
  if (!target.closest('.dropdown-container')) {
    isDropdownOpen.value = false;
  }
};

const handleWindowBlur = () => {
  if (isDropdownOpen.value) {
    isDropdownOpen.value = false;
  }
};

const handleVisibilityChange = () => {
  if (document.hidden && isDropdownOpen.value) {
    isDropdownOpen.value = false;
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
</script>

<style scoped>
.tab-header {
  width: 100%;
  display: flex;
  align-items: center;
}

.tab-header>div {
  flex: 1 1 0;
}

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
  z-index: 9999;
  overflow: hidden;
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

.activetab-title {
  opacity: 0.9;
}
</style>