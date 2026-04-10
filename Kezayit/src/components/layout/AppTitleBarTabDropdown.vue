<script setup lang="ts">
import { ref, nextTick } from 'vue'
import {
  IconDismiss20Regular,
  IconHome20Regular,
  IconDocument20Regular,
  IconBook20Filled,
  IconSearch20Regular,
  IconLibrary20Regular,
  IconDocumentPdf20Regular,
  IconApps20Regular,
} from '@iconify-prerendered/vue-fluent'
import type { Tab } from '@/stores/tabStore'

const props = defineProps<{ tabs: Tab[]; activeTabId: string }>()
const emit = defineEmits<{ select: [id: string]; close: [id: string]; dismiss: [] }>()

const containerRef = ref<HTMLElement | null>(null)
const visibleTabs = () =>
  props.tabs.filter((t) => t.id !== props.activeTabId && t.route !== '/settings')

nextTick(() => containerRef.value?.focus())
</script>

<template>
  <div ref="containerRef" class="tab-dropdown" tabindex="0" @keydown.esc.stop="emit('dismiss')">
    <div v-for="tab in visibleTabs()" :key="tab.id" class="tab-row" @click="emit('select', tab.id)">
      <div class="tab-row-start">
        <IconHome20Regular v-if="tab.route === '/'" class="tab-icon" />
        <IconBook20Filled v-else-if="tab.route === '/book-view'" class="tab-icon book-icon" />
        <IconDocumentPdf20Regular v-else-if="tab.route === '/pdf-view'" class="tab-icon" />
        <IconBook20Filled v-else-if="tab.route === '/hebrewbooks'" class="tab-icon book-icon" />
        <IconSearch20Regular v-else-if="tab.route === '/search'" class="tab-icon" />
        <IconLibrary20Regular v-else-if="tab.route === '/books'" class="tab-icon" />
        <IconApps20Regular v-else-if="tab.route === '/workspaces'" class="tab-icon" />
        <IconDocument20Regular v-else class="tab-icon" />
      </div>
      <span class="tab-row-title">
        {{ tab.title }}
        <span v-if="tab.tocPath" class="tab-toc-path"> · {{ tab.tocPath }}</span>
      </span>
      <div class="tab-row-end">
        <button class="tab-close" @click.stop="emit('close', tab.id)" title="סגור">
          <IconDismiss20Regular />
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.tab-dropdown {
  position: absolute;
  top: 100%;
  left: 0;
  right: 0;
  background: var(--bg-secondary);
  border-bottom: 1px solid var(--border-color);
  z-index: 100;
  max-height: 50vh;
  overflow-y: auto;
}

.tab-row {
  display: flex;
  align-items: center;
  height: 40px;
  padding: 0 4px;
  cursor: pointer;
  border-top: 1px solid var(--border-color);
  transition: background 120ms;
}
.tab-row:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}

.tab-row-start {
  display: flex;
  align-items: center;
  flex: 1;
  padding-inline-start: 4px;
  color: var(--text-secondary);
}
.tab-icon {
  width: 16px;
  height: 16px;
}
.book-icon {
  transform: scaleX(-1);
  color: #c1440e;
}
.tab-row-title {
  font-weight: 400;
  font-size: 0.82rem;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  min-width: 0;
}
.tab-toc-path {
  color: var(--text-secondary);
  opacity: 0.7;
}
.tab-row-end {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex: 1;
}

.tab-close {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  padding: 6px;
  border-radius: 4px;
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
}
.tab-close svg {
  width: 16px;
  height: 16px;
}
.tab-close:hover {
  background: var(--hover-bg);
  color: var(--text-primary);
}
</style>
