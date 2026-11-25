<script setup lang="ts">
import { ref, type Ref } from 'vue'
import { IconDismiss20Regular, IconReOrderDotsVertical20Regular } from '@iconify-prerendered/vue-fluent'
import { usePdfOcrStore } from '@/stores/pdfOcrStore'
import type { OcrScript } from './pdfOcrTypes'
import { useFloatingBar } from './useFloatingBar'
import { PDF_KEYS, withKey } from './usePdfReaderShortcuts'

/**
 * The OCR mode's bar for the new viewer: script picker, force, close - floating on one line,
 * dragged anywhere in the app like the editing bar. The dimming overlay stays over the pages.
 */
const props = defineProps<{ ocr: { isActive: Ref<boolean>; deactivate: () => void } }>()
const pdfOcrStore = usePdfOcrStore()

const SCRIPTS: { value: OcrScript; label: string; title: string }[] = [
  { value: 'hebrew', label: 'pdfViewer.ocr.scriptHebrew', title: 'pdfViewer.ocr.scriptHebrewTitle' },
  { value: 'rashi', label: 'pdfViewer.ocr.scriptRashi', title: 'pdfViewer.ocr.scriptRashiTitle' },
  { value: 'mixed', label: 'pdfViewer.ocr.scriptMixed', title: 'pdfViewer.ocr.scriptMixedTitle' },
]

const bar = ref<HTMLElement | null>(null)
const anchor = ref<HTMLElement | null>(null)
const { hostWindow, pos, dragHandlers } = useFloatingBar(bar, anchor, { x: 12, y: 56 })
void props
</script>

<template>
  <div v-if="ocr.isActive.value" class="ocr-overlay" />
  <span ref="anchor" class="ocr-anchor" />
  <Teleport :to="hostWindow.document.body">
    <div
      v-if="ocr.isActive.value"
      ref="bar"
      class="pdf-ocr-bar"
      dir="rtl"
      :style="{ left: `${pos.x}px`, top: `${pos.y}px` }"
      v-on="dragHandlers"
    >
      <span class="grip" :title="$t('bookView.toolbar.dragHint')"><IconReOrderDotsVertical20Regular /></span>
      <div class="script-buttons">
        <button
          v-for="s in SCRIPTS"
          :key="s.value"
          class="script-btn"
          :class="{ active: pdfOcrStore.script === s.value }"
          :title="withKey($t(s.title), PDF_KEYS.ocrScript)"
          @click="pdfOcrStore.setScript(s.value)"
        >
          {{ $t(s.label) }}
        </button>
        <button class="script-btn" :class="{ active: pdfOcrStore.script === 'english' }" :title="withKey('English', PDF_KEYS.ocrScript)" @click="pdfOcrStore.setScript('english')">
          English
        </button>
      </div>
      <button
        class="toggle-btn"
        :class="{ active: pdfOcrStore.skipExistingText }"
        :title="withKey($t('pdfViewer.ocr.forceTitle'), PDF_KEYS.ocrForce)"
        @click="pdfOcrStore.toggleSkipExistingText()"
      >
        {{ $t('pdfViewer.ocr.force') }}
      </button>
      <button class="close-btn" :title="$t('pdfViewer.closeEsc')" @click="ocr.deactivate()">
        <IconDismiss20Regular />
      </button>
    </div>
  </Teleport>
</template>

<style scoped>
.ocr-anchor {
  display: none;
}
.ocr-overlay {
  position: absolute;
  inset: 0;
  background: rgba(0, 0, 0, 0.2);
  pointer-events: none;
  z-index: 8000;
}
/* The editing bar's floating look (PdfEditToolbar.vue), on one line. */
.pdf-ocr-bar {
  position: fixed;
  z-index: 9000;
  display: flex;
  flex-wrap: nowrap;
  align-items: center;
  gap: 6px;
  /* the editing bar's height: 26px controls + 2px padding + border */
  height: 32px;
  box-sizing: border-box;
  padding: 2px 6px 2px 2px;
  background: var(--bg-toolbar);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.18);
  white-space: nowrap;
  cursor: grab;
  touch-action: none;
  user-select: none;
}
.grip {
  display: flex;
  align-items: center;
  height: 26px;
  opacity: 0.6;
  cursor: grab;
}
.grip svg {
  width: 14px;
  height: 14px;
}
.script-buttons {
  display: flex;
  gap: 1px;
}
.script-btn,
.toggle-btn {
  height: 24px;
  padding: 0 10px;
  font-size: 12px;
  font-weight: 500;
  color: var(--text-secondary);
  background: none;
  border: none;
  border-radius: 4px;
  white-space: nowrap;
  cursor: pointer;
}
.script-btn:hover,
.toggle-btn:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 4%, transparent);
}
/* flat, as the toolbar buttons: the chosen one in the accent colour on a light wash */
.script-btn.active,
.toggle-btn.active {
  color: var(--accent-color);
  background: color-mix(in srgb, var(--accent-color) 14%, transparent);
}
.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  padding: 0;
  border: none;
  border-radius: 4px;
  background: none;
  color: var(--text-secondary);
  cursor: pointer;
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
