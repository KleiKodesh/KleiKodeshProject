<template>
  <div class="bar c-pointer tab-header"
       @click.stop="handleHeaderClick">
    <div class="flex-row">
      <!-- Settings menu - COMMENTED OUT: Moved first 3 items inline
      <TabHeaderMenu @close="handleDropdownClose" />
      -->

      <button v-if="tabStore.activeTab?.currentPage === 'bookview'"
              @click.stop="handleButtonClick(goToToc)"
              class="flex-center c-pointer touch-interactive"
              title="תוכן עניינים">
        <Icon icon="fluent:text-bullet-list-tree-24-regular"
              class="rtl-flip" />
      </button>

      <!-- Diacritics toggle button -->
      <button v-if="tabStore.activeTab?.currentPage === 'bookview'"
              @click.stop="handleButtonClick(handleDiacriticsToggle)"
              class="flex-center c-pointer touch-interactive"
              :title="diacriticsLabel">
        <component :is="diacriticsIconComponent"
                   class="diacritics-icon"
                   :class="diacriticsStateClass" />
      </button>

      <!-- Alt TOC toggle button -->
      <button v-if="tabStore.activeTab?.currentPage === 'bookview'"
              @click.stop="handleButtonClick(handleAltTocToggle)"
              class="flex-center c-pointer touch-interactive"
              :title="isAltTocVisible ? 'הסתר כותרות נוספות' : 'הצג כותרות נוספות'">
        <Icon icon="fluent:eye-lines-28-regular" />
      </button>

      <button v-if="tabStore.activeTab?.currentPage === 'bookview' && hasConnections && !isTocVisible"
              @click.stop="handleButtonClick(toggleSplitPane)"
              class="flex-center c-pointer touch-interactive"
              :title="isSplitPaneOpen ? 'הסתר קשרים' : 'הצג קשרים'">
        <Icon
              :icon="isSplitPaneOpen ? 'fluent:panel-bottom-expand-20-filled' : 'fluent:panel-bottom-contract-20-filled'" />
      </button>

    </div>
    <span class="center-text ellipsis activetab-title"
          title="הצג רשימת טאבים">{{ tabStore.activeTab?.title
          }}</span>

    <div class="flex-row justify-end">
      <button v-if="tabStore.activeTab?.currentPage === 'bookview' && !isTocVisible"
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
// import TabHeaderMenu from './TabHeaderMenu.vue'; // Commented out
import DiacriticsFullIcon from './icons/DiacriticsFullIcon.vue';
import DiacriticsNikkudOnlyIcon from './icons/DiacriticsNikkudOnlyIcon.vue';
import DiacriticsNoneIcon from './icons/DiacriticsNoneIcon.vue';
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

// Alt TOC visibility state
const isAltTocVisible = computed(() => {
  const bookState = tabStore.activeTab?.bookState;
  if (!bookState) return true;
  return bookState.showAltToc !== false;
});

// Diacritics state - centralized in tabStore
const diacriticsState = computed(() => tabStore.currentDiacriticsState);

const diacriticsStateClass = computed(() => {
  if (diacriticsState.value === 1) return 'state-1';
  if (diacriticsState.value === 2) return 'state-2';
  return '';
});

const diacriticsIconComponent = computed(() => {
  if (diacriticsState.value === 1) return DiacriticsNikkudOnlyIcon;
  if (diacriticsState.value === 2) return DiacriticsNoneIcon;
  return DiacriticsFullIcon;
});

const diacriticsLabel = computed(() => {
  if (diacriticsState.value === 0) return 'הסר טעמים';
  if (diacriticsState.value === 1) return 'הסר גם ניקוד';
  return 'שחזר טעמים וניקוד';
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

const handleAltTocToggle = () => {
  tabStore.toggleAltTocDisplay();
};

const handleDiacriticsToggle = () => {
  tabStore.toggleDiacritics();
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

.diacritics-icon {
  flex-shrink: 0;
  width: 20px;
  height: 20px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-primary);
}

.diacritics-icon :deep(svg) {
  fill: currentColor;
}

.diacritics-icon.state-1 :deep(svg) {
  fill: #ff8c00;
}

.diacritics-icon.state-2 :deep(svg) {
  fill: #ff4500;
}
</style>