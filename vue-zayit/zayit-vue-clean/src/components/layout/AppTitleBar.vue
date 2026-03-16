<script setup lang="ts">
import { ref, computed } from 'vue'
import { onClickOutside } from '@vueuse/core'
import { IconLineHorizontal320Regular, IconAdd20Regular, IconDismiss20Regular, IconHome20Regular, IconLayoutRowTwo20Regular } from '@iconify-prerendered/vue-fluent'
import ThemeToggle from '@/theme/ThemeToggle.vue'
import AppTitleBarTabDropdown from './AppTitleBarTabDropdown.vue'
import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'

const bookViewStore = useBookViewStore()

const tabStore = useTabStore()
const activeTab = computed(() => tabStore.activeTab)
const dropdownOpen = ref(false)
const barRef = ref<HTMLElement | null>(null)

onClickOutside(barRef, () => { dropdownOpen.value = false })

function selectTab(id: string) { tabStore.switchTab(id); dropdownOpen.value = false }

function goHome() {
  const existing = tabStore.tabs.find(t => t.route === '/')
  if (existing) {
    const cur = tabStore.activeTabId.value
    tabStore.switchTab(existing.id)
    if (cur !== existing.id) tabStore.closeTab(cur)
  } else {
    tabStore.updateActiveTab({ route: '/', title: 'בית' })
  }
}
</script>

<template>
  <header ref="barRef" class="title-bar" @click="dropdownOpen = !dropdownOpen">
    <div class="bar-start">
      <button class="bar-btn" @click.stop><IconLineHorizontal320Regular /></button>
      <ThemeToggle @click.stop />
            <button
        v-if="bookViewStore.isBookViewActive"
        class="bar-btn"
        :class="{ active: bookViewStore.toolbarVisible }"
        title="סרגל כלים"
        @click.stop="bookViewStore.toggleToolbar"
      >
        <IconLayoutRowTwo20Regular />
      </button>
    </div>

    <span class="bar-title" :title="bookViewStore.currentTocPath ? `${activeTab?.title} · ${bookViewStore.currentTocPath}` : activeTab?.title">
      {{ activeTab?.title }}
      <span v-if="bookViewStore.currentTocPath" class="bar-toc-path"> · {{ bookViewStore.currentTocPath }}</span>
    </span>

    <div class="bar-end">
      <button class="bar-btn" title="בית" @click.stop="goHome"><IconHome20Regular /></button>
      <button class="bar-btn" title="לשונית חדשה" @click.stop="tabStore.openNewHomeTab"><IconAdd20Regular /></button>
      <button class="bar-btn" title="סגור לשונית" @click.stop="tabStore.closeTab(tabStore.activeTabId)"><IconDismiss20Regular /></button>
    </div>

    <AppTitleBarTabDropdown
      v-if="dropdownOpen"
      :tabs="tabStore.tabs"
      :active-tab-id="tabStore.activeTabId"
      @select="selectTab"
      @close="tabStore.closeTab"
      @click.stop
    />
  </header>
</template>

<style scoped>
.title-bar {
  display: flex;
  align-items: center;
  height: 40px;
  padding: 0 4px;
  background: var(--bg-secondary);
  border-bottom: 1px solid var(--border-color);
  position: relative;
  cursor: pointer;
}

.bar-start { display: flex; align-items: center; gap: 0; flex: 1; }
.bar-end { display: flex; align-items: center; justify-content: flex-end; gap: 0; flex: 1; }
.bar-title { font-weight: 600; font-size: 0.9rem; color: var(--text-primary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.bar-toc-path { font-weight: 400; color: var(--text-secondary); }

.bar-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  padding: 6px;
}
.bar-btn.active { color: var(--accent-color); }
</style>
