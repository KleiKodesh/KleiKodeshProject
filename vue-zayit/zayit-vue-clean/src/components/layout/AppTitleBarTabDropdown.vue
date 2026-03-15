<script setup lang="ts">
import { IconDismiss20Regular, IconHome20Regular, IconDocument20Regular } from '@iconify-prerendered/vue-fluent'
import type { Tab } from '@/stores/tabStore'

defineProps<{ tabs: Tab[]; activeTabId: string }>()
defineEmits<{ select: [id: string]; close: [id: string] }>()
</script>

<template>
  <div class="tab-dropdown">
    <div
      v-for="tab in tabs.filter(t => t.id !== activeTabId && t.route !== '/settings')"
      :key="tab.id"
      class="tab-row"
      @click="$emit('select', tab.id)"
    >
      <div class="tab-row-start">
        <IconHome20Regular v-if="tab.route === '/'" class="tab-icon" />
        <IconDocument20Regular v-else class="tab-icon" />
      </div>
      <span class="tab-row-title">{{ tab.title }}</span>
      <div class="tab-row-end">
        <button class="tab-close" @click.stop="$emit('close', tab.id)" title="סגור">
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
  inset-inline-start: 0;
  inset-inline-end: 0;
  background: var(--bg-secondary);
  border-bottom: 1px solid var(--border-color);
  z-index: 100;
  max-height: 50vh;
  overflow-y: auto;
}

.tab-row {
  display: flex;
  align-items: center;
  height: 48px;
  padding: 0 8px;
  cursor: pointer;
  border-top: 1px solid var(--border-color);
  transition: background 120ms;
}
.tab-row:hover { background: var(--hover-bg); }

.tab-row-start { display: flex; align-items: center; flex: 1; padding-inline-start: 4px; color: var(--text-secondary); }
.tab-icon { width: 20px; height: 20px; }
.tab-row-title { font-weight: 600; font-size: 1rem; color: var(--text-primary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; min-width: 0; }
.tab-row-end { display: flex; align-items: center; justify-content: flex-end; flex: 1; }

.tab-close {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  padding: 6px;
  border: none;
  border-radius: 4px;
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  transition: background 120ms, color 120ms;
}
.tab-close:hover { background: var(--hover-bg); color: var(--text-primary); }
</style>
