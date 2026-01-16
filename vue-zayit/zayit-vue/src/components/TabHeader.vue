<template>
  <div class="bar c-pointer tab-header">
    <div class="flex-row">
      <!-- Settings menu -->
      <TabHeaderMenu @close="handleDropdownClose" />

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
    <span class="center-text ellipsis activetab-title c-pointer"
          @click.stop="handleTitleClick"
          title="הצג רשימת טאבים">{{ tabStore.activeTab?.title
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
import { computed } from 'vue';
import { Icon } from '@iconify/vue';
import TabHeaderMenu from './TabHeaderMenu.vue';
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

const handleTitleClick = () => {
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