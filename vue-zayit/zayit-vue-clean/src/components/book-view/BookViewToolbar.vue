<script setup lang="ts">
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import { IconTextBulletListTree20Regular } from '@iconify-prerendered/vue-fluent'
import { IconPanelBottom20Regular } from '@iconify-prerendered/vue-fluent'

defineProps<{ bottomVisible: boolean; searchVisible: boolean; tocVisible: boolean }>()
defineEmits<{ toggleBottom: []; toggleSearch: []; toggleToc: [] }>()
</script>

<template>
  <div class="book-view-toolbar">
    <button @click="$emit('toggleSearch')" :class="{ active: searchVisible }">
      <IconSearch20Regular />
    </button>
    <button @click="$emit('toggleToc')" :class="{ active: tocVisible }">
      <IconTextBulletListTree20Regular />
    </button>
    <button @click="$emit('toggleBottom')" :class="{ active: bottomVisible }">
      <IconPanelBottom20Regular />
    </button>
  </div>
</template>

<style scoped>
.book-view-toolbar {
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0;
  padding-inline: 4px;
  background: var(--bg-toolbar);
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
  transition: background 120ms;
}
button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  padding: 4px;
}
button.active { color: var(--accent-color); }
</style>
