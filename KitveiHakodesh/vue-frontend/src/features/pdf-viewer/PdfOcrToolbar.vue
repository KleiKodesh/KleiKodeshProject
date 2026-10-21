<script setup lang="ts">
/**
 * The OCR selection mode's chrome over a PDF.js iframe: the dimming overlay and the
 * script / force-OCR toolbar. Shared by the PDF tab and the book view's side-by-side
 * HebrewBooks edition; the host must be position: relative around its iframe.
 */
import { IconDismiss20Regular } from '@iconify-prerendered/vue-fluent'
import { usePdfOcrStore } from '@/stores/pdfOcrStore'
import type { usePdfOcrSelection } from './usePdfOcrSelection'

defineProps<{
  ocr: ReturnType<typeof usePdfOcrSelection>
  /** Pin the toolbar to the host box (a side panel) instead of the window's top centre. */
  contained?: boolean
}>()

const pdfOcrStore = usePdfOcrStore()
</script>

<template>
  <div v-if="ocr.isActive.value" class="ocr-overlay" />
  <div v-if="ocr.isActive.value" class="ocr-toolbar" :class="{ 'ocr-toolbar-contained': contained }">
    <div class="toolbar-content">
      <div class="script-buttons">
        <button
          class="script-btn"
          :class="{ active: pdfOcrStore.script === 'hebrew' }"
          @click="pdfOcrStore.setScript('hebrew')"
          :title="$t('pdfViewer.ocr.scriptHebrewTitle')"
        >
          {{ $t('pdfViewer.ocr.scriptHebrew') }}
        </button>
        <button
          class="script-btn"
          :class="{ active: pdfOcrStore.script === 'rashi' }"
          @click="pdfOcrStore.setScript('rashi')"
          :title="$t('pdfViewer.ocr.scriptRashiTitle')"
        >
          {{ $t('pdfViewer.ocr.scriptRashi') }}
        </button>
        <button
          class="script-btn"
          :class="{ active: pdfOcrStore.script === 'mixed' }"
          @click="pdfOcrStore.setScript('mixed')"
          :title="$t('pdfViewer.ocr.scriptMixedTitle')"
        >
          {{ $t('pdfViewer.ocr.scriptMixed') }}
        </button>
        <button
          class="script-btn"
          :class="{ active: pdfOcrStore.script === 'english' }"
          @click="pdfOcrStore.setScript('english')"
          title="English"
        >
          English
        </button>
      </div>
      <button
        class="toggle-btn"
        :class="{ active: pdfOcrStore.skipExistingText }"
        @click="pdfOcrStore.toggleSkipExistingText()"
        :title="$t('pdfViewer.ocr.forceTitle')"
      >
        {{ $t('pdfViewer.ocr.force') }}
      </button>
      <button class="close-btn" @click="ocr.deactivate()" :title="$t('pdfViewer.closeEsc')">
        <IconDismiss20Regular />
      </button>
    </div>
  </div>
</template>

<style scoped>
.ocr-overlay {
  position: absolute;
  inset: 0;
  background: rgba(0, 0, 0, 0.2);
  pointer-events: none;
  z-index: 8000;
}

.ocr-toolbar {
  position: fixed;
  top: 12px;
  left: 50%;
  transform: translateX(-50%);
  z-index: 10000;
  animation: slideDown 200ms cubic-bezier(0.16, 1, 0.3, 1);
}

.ocr-toolbar-contained {
  position: absolute;
  max-width: calc(100% - 16px);
}
.ocr-toolbar-contained .toolbar-content {
  flex-wrap: wrap;
  justify-content: center;
}

@keyframes slideDown {
  from {
    opacity: 0;
    transform: translateX(-50%) translateY(-10px);
  }
  to {
    opacity: 1;
    transform: translateX(-50%) translateY(0);
  }
}

.toolbar-content {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 16px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.3);
}

.script-buttons {
  display: flex;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  overflow: hidden;
  background: var(--bg-primary);
}

.script-btn {
  padding: 4px 12px;
  font-size: 12px;
  font-weight: 500;
  color: var(--text-secondary);
  background: none;
  border: none;
  cursor: pointer;
  transition: all 100ms ease;
}

.script-btn:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 4%, transparent);
}

.script-btn.active {
  background: var(--accent-color);
  color: #fff;
}

.toggle-btn {
  padding: 4px 12px;
  font-size: 12px;
  font-weight: 500;
  color: var(--text-secondary);
  background: none;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  cursor: pointer;
  transition: all 100ms ease;
  white-space: nowrap;
}

.toggle-btn:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 4%, transparent);
}

.toggle-btn.active {
  background: var(--status-warning);
  color: #fff;
  border-color: var(--status-warning);
}

.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  padding: 0;
  border-radius: 4px;
  border: none;
  background: none;
  color: var(--text-secondary);
  cursor: pointer;
  transition: all 100ms ease;
}

.close-btn:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
  color: var(--text-primary);
}

.close-btn svg {
  width: 14px;
  height: 14px;
}
</style>
