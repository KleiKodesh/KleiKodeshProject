<script setup lang="ts">
import type { Component } from 'vue'
import {
  IconTextBulletListTree16Regular,
  IconBookmark16Regular,
  IconComment16Regular,
  IconDocumentMultiple16Regular,
} from '@iconify-prerendered/vue-fluent'
import { PDF_KEYS, withKey } from './usePdfReaderShortcuts'

export type PdfSideMode = 'outline' | 'bookmarks' | 'comments' | 'thumbnails'

/** The side panel's mode switch: a row of the toolbar's icon buttons above the panel's content. */
const mode = defineModel<PdfSideMode>({ required: true })

const MODES: { id: PdfSideMode; icon: Component; label: string }[] = [
  { id: 'outline', icon: IconTextBulletListTree16Regular, label: 'pdfReader.panel.outline' },
  { id: 'bookmarks', icon: IconBookmark16Regular, label: 'pdfReader.panel.bookmarks' },
  { id: 'comments', icon: IconComment16Regular, label: 'pdfReader.panel.comments' },
  { id: 'thumbnails', icon: IconDocumentMultiple16Regular, label: 'pdfReader.panel.thumbnails' },
]
</script>

<template>
  <div class="side-modes" role="tablist">
    <button
      v-for="m in MODES"
      :key="m.id"
      type="button"
      role="tab"
      class="side-mode"
      :class="{ selected: mode === m.id }"
      :aria-selected="mode === m.id"
      :title="withKey($t(m.label), PDF_KEYS.side[m.id])"
      :aria-label="$t(m.label)"
      @click="mode = m.id"
    >
      <component :is="m.icon" />
    </button>
  </div>
</template>

<style scoped>
.side-modes {
  display: flex;
  justify-content: center;
  gap: 2px;
  flex-shrink: 0;
  padding: 4px;
  border-bottom: 1px solid var(--border-color);
}
.side-mode {
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--toolbar-button-size, 28px);
  height: var(--toolbar-button-size, 28px);
  padding: 6px;
  border-radius: 4px;
  color: var(--text-secondary);
}
.side-mode:hover {
  color: var(--text-primary);
}
.side-mode.selected {
  color: var(--accent-color);
  background: var(--hover-bg);
}
</style>
