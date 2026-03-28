<script setup lang="ts">
import { ref, nextTick } from 'vue'
import {
  IconDismiss20Regular,
  IconHome20Regular,
  IconDocument20Regular,
  IconBookOpen20Filled,
  IconSearch20Regular,
  IconLibrary20Filled,
  IconDocumentPdf20Regular,
  IconApps20Filled,
} from '@iconify-prerendered/vue-fluent'
import { useListKeys } from '@/composables/useListKeys'
import type { Tab } from '@/stores/tabStore'

const props = defineProps<{ tabs: Tab[]; activeTabId: string }>()
const emit = defineEmits<{ select: [id: string]; close: [id: string]; dismiss: [] }>()

const containerRef = ref<HTMLElement | null>(null)
const visibleTabs = () => props.tabs.filter(t => t.id !== props.activeTabId && t.route !== '/settings')

const { focusedIndex, containerFocused } = useListKeys(
  containerRef,
  () => visibleTabs().length,
  (i) => emit('select', visibleTabs()[i]!.id),
)

nextTick(() => containerRef.value?.focus())
</script>

<template>
  <div ref="containerRef" class="tab-dropdown" tabindex="0" @keydown.esc.stop="emit('dismiss')">
    <div
      v-for="(tab, i) in visibleTabs()"
      :key="tab.id"
      class="tab-row"
      data-nav-item
      :class="{ 'is-focused': containerFocused && focusedIndex === i }"
      @click="focusedIndex = i; emit('select', tab.id)"
      @keydown.enter="emit('select', tab.id)"
    >
      <div class="tab-row-start">
        <IconHome20Regular v-if="tab.route === '/'" class="tab-icon" />
        <IconDocument20Regular v-else-if="tab.route === '/book-view'" class="tab-icon" />
        <IconDocumentPdf20Regular v-else-if="tab.route === '/pdf-view'" class="tab-icon" />
        <IconBookOpen20Filled v-else-if="tab.route === '/hebrewbooks'" class="tab-icon" />
        <IconSearch20Regular v-else-if="tab.route === '/search'" class="tab-icon" />
        <IconLibrary20Filled v-else-if="tab.route === '/books'" class="tab-icon" />
        <IconApps20Filled v-else-if="tab.route === '/workspaces'" class="tab-icon" />
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
  height: 32px;
  padding: 0 4px;
  cursor: pointer;
  border-top: 1px solid var(--border-color);
  transition: background 120ms;
}
.tab-row:hover { background: var(--hover-bg); }

.tab-row-start { display: flex; align-items: center; flex: 1; padding-inline-start: 4px; color: var(--text-secondary); }
.tab-icon { width: 14px; height: 14px; }
.tab-row-title { font-weight: 400; font-size: 0.82rem; color: var(--text-primary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; min-width: 0; }
.tab-toc-path { color: var(--text-secondary); }
.tab-row-end { display: flex; align-items: center; justify-content: flex-end; flex: 1; }

.tab-close {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  padding: 3px;
  border-radius: 0;
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
}
.tab-close svg { width: 14px; height: 14px; }
.tab-close:hover { background: var(--hover-bg); color: var(--text-primary); }
</style>
