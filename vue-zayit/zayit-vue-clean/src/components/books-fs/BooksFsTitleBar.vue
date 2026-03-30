<script setup lang="ts">
import {
  IconTextBulletList20Regular,
  IconGrid20Regular,
  IconTextBulletListTree20Regular,
  IconHome16Regular,
} from '@iconify-prerendered/vue-fluent'
import BooksBreadcrumb from './BooksBreadcrumb.vue'
import type { CategoryNode } from './booksFsTree'
defineProps<{ view: 'list' | 'tiles' | 'tree'; path: CategoryNode[]; isSearching: boolean }>()
defineEmits<{ setView: ['list' | 'tiles' | 'tree']; navigate: [number]; reset: [] }>()
</script>

<template>
  <div class="titlebar">
    <BooksBreadcrumb
      v-if="view !== 'tree' || isSearching"
      :path="path"
      @navigate="$emit('navigate', $event)"
    />
    <button v-else class="home-btn" title="איפוס" @click="$emit('reset')">
      <IconHome16Regular />
    </button>
    <div class="view-switcher">
      <button
        :class="{ active: view === 'list' }"
        title="תצוגת רשימה"
        @click="$emit('setView', 'list')"
      >
        <IconTextBulletList20Regular />
      </button>
      <button
        :class="{ active: view === 'tiles' }"
        title="תצוגת אריחים"
        @click="$emit('setView', 'tiles')"
      >
        <IconGrid20Regular />
      </button>
      <button
        :class="{ active: view === 'tree' }"
        title="תצוגת עץ"
        @click="$emit('setView', 'tree')"
      >
        <IconTextBulletListTree20Regular class="rtl-flip" />
      </button>
    </div>
  </div>
</template>

<style scoped>
.titlebar {
  display: flex;
  align-items: stretch;
  border-bottom: 1px solid var(--border-color);
  background: var(--bg-toolbar);
  min-height: 32px;
}
.home-btn {
  display: inline-flex;
  align-items: center;
  padding: 0 6px;
  height: 24px;
  border-radius: 0;
  color: var(--text-secondary);
  margin-inline-start: 4px;
  margin-inline-end: auto;
  align-self: center;
}
.home-btn:hover {
  color: var(--text-primary);
}
.home-btn svg {
  width: 16px;
  height: 16px;
}
.view-switcher {
  display: flex;
  align-items: stretch;
  flex-shrink: 0;
}
.view-switcher button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 100%;
  padding: 6px;
  border-radius: 0;
  border-bottom: 2px solid transparent;
}
.view-switcher button svg {
  width: 16px;
  height: 16px;
}
.view-switcher button.active {
  color: var(--accent-color);
  border-bottom-color: var(--accent-color);
}
.rtl-flip {
  transform: scaleX(-1);
}
</style>
