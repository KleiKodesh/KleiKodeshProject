<template>
  <div class="bar c-pointer tab-header"
       @click.stop="handleHeaderClick">
    <div class="flex-row">
      <!-- Settings menu -->
      <TitlebarDropdownMenu @close="handleDropdownClose" />

      <!-- Theme toggle for PDF and Hebrew books pages -->
      <ThemeToggleButton v-if="isPdfPage || isHebrewBooksPage"
                         @click.stop />

      <!-- Toolbar toggle button -->
      <button v-if="tabStore.activeTab?.currentPage === 'bookview'"
              @click.stop="handleButtonClick(toggleToolbar)"
              class="flex-center c-pointer touch-interactive"
              :title="isToolbarVisible ? 'הסתר סרגל כלים' : 'הצג סרגל כלים'">
        <Icon :icon="isToolbarVisible ? 'fluent:options-24-filled' : 'fluent:options-24-regular'" />
      </button>

      <button v-if="tabStore.activeTab?.currentPage === 'bookview'"
              @click.stop="handleButtonClick(goToToc)"
              class="flex-center c-pointer touch-interactive"
              title="תוכן עניינים">
        <Icon icon="fluent:text-bullet-list-tree-24-regular"
              class="rtl-flip" />
      </button>

      <button v-if="tabStore.activeTab?.currentPage === 'bookview' && hasConnections && !isTocVisible && !isToolbarVisible"
              @click.stop="handleButtonClick(toggleSplitPane)"
              class="flex-center c-pointer touch-interactive"
              :title="isSplitPaneOpen ? 'הסתר מפרשים וקישורים' : 'הצג מפרשים וקישורים'">
        <CommentaryToggleIcon :is-open="isSplitPaneOpen" />
      </button>

    </div>
    <span class="center-text ellipsis activetab-title"
          title="הצג רשימת טאבים">{{ tabStore.activeTab?.title
          }}</span>

    <div class="flex-row justify-end">
      <button v-if="tabStore.activeTab?.currentPage === 'bookview' && !isTocVisible && !isToolbarVisible"
              @click.stop="handleButtonClick(openSearch)"
              class="flex-center c-pointer touch-interactive"
              title="חיפוש (Ctrl+F)">
        <Icon icon="fluent:search-28-filled" />
      </button>

      <button @click.stop="handleButtonClick(resetTab)"
              class="flex-center c-pointer touch-interactive"
              title="דף הבית">
        <Icon icon="fluent:home-28-regular" />
      </button>

      <button @click.stop="handleButtonClick(newTab)"
              class="flex-center c-pointer touch-interactive"
              title="פתח טאב חדש">
        <Icon icon="fluent:add-16-filled"
              class="small-icon" />
      </button>

      <button @click.stop="handleButtonClick(closeTab)"
              class="flex-center c-pointer touch-interactive"
              title="סגור">
        <Icon icon="fluent:dismiss-16-filled"
              class="small-icon" />
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { Icon } from '@iconify/vue';
import TitlebarDropdownMenu from './TitlebarDropdownMenu.vue';
import CommentaryToggleIcon from '@/components/icons/CommentaryToggleIcon.vue';
import ThemeToggleButton from './ThemeToggleButton.vue';
import { useTabStore } from '../stores/tabStore';

const tabStore = useTabStore();

const emit = defineEmits<{
  'click': []
  'close-tab-dropdown': []
}>();

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

const isToolbarVisible = computed(() => {
  const bookState = tabStore.activeTab?.bookState;
  if (!bookState) return true; // Default to visible
  return bookState.showToolbar !== false; // Default to true if undefined
});

const isPdfPage = computed(() => {
  return tabStore.activeTab?.currentPage === 'pdfview';
});

const isHebrewBooksPage = computed(() => {
  return tabStore.activeTab?.currentPage === 'hebrewbooks-view';
});

const handleHeaderClick = () => {
  emit('click');
};

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

const toggleToolbar = () => {
  tabStore.toggleToolbar();
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

const handleButtonClick = (action: () => void) => {
  action();
  emit('close-tab-dropdown');
};

const handleDropdownClose = () => {
  emit('close-tab-dropdown');
};
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

.activetab-title {
  opacity: 0.9;
}
</style>