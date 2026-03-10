<template>
  <div v-if="hasVisibleItems"
       ref="dropdownContainer"
       class="dropdown-container">
    <button @click.stop="toggleDropdown"
            class="flex-center c-pointer dropdown-toggle"
            title="אפשרויות">
      <Icon icon="fluent:more-vertical-28-regular" />
    </button>

    <transition name="slide">
      <div v-if="isOpen"
           class="dropdown-menu">
        <div class="dropdown-content">
          <!-- Alt TOC toggle dropdown item - moved to toolbar -->
          <!-- <div v-if="tabStore.activeTab?.currentPage === 'bookview' && !isToolbarVisible"
               @click.stop="handleAltTocToggleClick"
               class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
            <Icon icon="fluent:eye-lines-28-regular" />
            <span class="dropdown-label">{{ isAltTocVisible ? 'הסתר כותרות נוספות' : 'הצג כותרות נוספות' }}</span>
          </div> -->

          <!-- Diacritics toggle dropdown item - moved to toolbar -->
          <!-- <TitlebarDiacriticsDropdown :hide-when-toolbar-visible="true" /> -->

          <!-- Line display toggle dropdown item -->
          <!-- COMMENTED OUT: Block view functionality removed for performance
          <div v-if="tabStore.activeTab?.currentPage === 'bookview'"
               @click.stop="handleLineDisplayClick"
               class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
            <Icon icon="fluent:text-align-right-24-regular"
                  v-if="isLineDisplayInline" />
            <Icon icon="fluent:text-align-justify-24-regular"
                  v-else />
            <span class="dropdown-label">{{ isLineDisplayInline ? 'תצוגת בלוק' : 'תצוגת שורה' }}</span>
          </div>
          -->

          <!-- Theme toggle - only on bookview page -->
          <ThemeDropdownItem v-if="isBookViewPage"
                             @click.stop />

          <div v-if="!isHomepage"
               @click.stop="handleSettingsClick"
               class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
            <Icon icon="fluent:settings-28-regular" />
            <span class="dropdown-label">הגדרות</span>
          </div>

          <div v-if="!isHomepage"
               @click.stop="handleWorkspaceManagerClick"
               class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
            <Icon icon="fluent:apps-28-regular" />
            <span class="dropdown-label">ניהול סביבות עבודה</span>
          </div>

          <!-- Search Page -->
          <div v-if="!isHomepage"
               @click.stop="handleSearchPageClick"
               class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
            <Icon icon="fluent:search-sparkle-24-filled" />
            <span class="dropdown-label">חיפוש כללי</span>
          </div>

          <!-- Open Book - only show when not on homepage -->
          <div v-if="!isHomepage"
               @click.stop="handleOpenBookClick"
               class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
            <Icon icon="fluent:library-28-regular" />
            <span class="dropdown-label">פתח ספר</span>
          </div>

          <!-- PDF viewer - only show on open file page, not homepage -->
          <div v-if="!isHomepage"
               @click.stop="handleOpenPdfClick"
               class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
            <Icon icon="fluent:document-pdf-28-regular" />
            <span class="dropdown-label">פתח PDF</span>
          </div>

          <!-- Hebrew Books - only show on open file page, not homepage-->
          <div v-if="!isHomepage"
               @click.stop="handleHebrewBooksClick"
               class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
            <Icon icon="fluent:book-open-48-regular" />
            <span class="dropdown-label">היברו-בוקס</span>
          </div>

          <!-- Popout toggle - only available in C# WebView -->
          <div v-if="isWebViewAvailable"
               @click.stop="handlePopoutClick"
               class="flex-row flex-center-start hover-bg c-pointer dropdown-item"
               title="פתח בחלון עצמאי או החזר לחלונית צד של Word">
            <Icon icon="fluent:open-28-regular" />
            <span class="dropdown-label">הצג בחלונית</span>
          </div>
        </div>
      </div>
    </transition>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, computed } from 'vue';
import { Icon } from '@iconify/vue';
import { useTitlebarDropdown } from '@/components/workspace/useTitlebarDropdown';
import ThemeDropdownItem from '@/components/settings/ThemeDropdownItem.vue';

const emit = defineEmits<{
  'close': []
}>();

const {
  isOpen,
  isWebViewAvailable,
  isHomepage,
  isBookViewPage,
  isLineDisplayInline,
  isAltTocVisible,
  isToolbarVisible,
  toggleDropdown,
  closeDropdown: close,
  handleSettingsClick,
  handleWorkspaceManagerClick,
  handleOpenBookClick,
  handleHebrewBooksClick,
  handleSearchPageClick,
  handleLineDisplayClick,
  handleAltTocToggleClick,
  handleOpenPdfClick,
  handlePopoutClick,
  setupClickOutside
} = useTitlebarDropdown();

const dropdownContainer = ref<HTMLElement>();

// Check if any dropdown items are visible
const hasVisibleItems = computed(() => {
  // Theme toggle on bookview page
  if (isBookViewPage.value) return true;

  // All other items show when not on homepage
  if (!isHomepage.value) return true;

  // Popout toggle when WebView is available
  if (isWebViewAvailable.value) return true;

  return false;
});

const closeDropdown = () => {
  close();
  emit('close');
};

onMounted(() => {
  if (dropdownContainer.value) {
    setupClickOutside(dropdownContainer.value);
  }
});

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