<script setup lang="ts">
import { ref } from 'vue'
import { IconDismiss20Regular, IconCopy20Regular } from '@iconify-prerendered/vue-fluent'
import type { OcrSelectionResult, OcrScript } from './usePdfOcrSelection'

const props = defineProps<{
  result: OcrSelectionResult
  script: OcrScript
}>()
const emit = defineEmits<{ dismiss: []; 'update:script': [OcrScript] }>()

const textRef = ref<HTMLTextAreaElement | null>(null)
const copied = ref(false)

async function copyText() {
  const text = textRef.value?.value ?? props.result.text
  await navigator.clipboard.writeText(text)
  copied.value = true
  setTimeout(() => { copied.value = false; emit('dismiss') }, 600)
}

function onOverlayClick(event: MouseEvent) {
  if (event.target === event.currentTarget) emit('dismiss')
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') emit('dismiss')
}
</script>

<template>
  <div class="popup-overlay" @click="onOverlayClick" @keydown="onKeydown">
    <div class="popup" dir="rtl">
      <div class="popup-header">
        <span class="popup-title">{{ result.isOcr ? 'טקסט מזוהה (OCR)' : 'טקסט נבחר' }}</span>
        <div class="script-toggle">
          <button class="script-btn" :class="{ active: script === 'hebrew' }" @click="emit('update:script', 'hebrew')">עברי</button>
          <button class="script-btn" :class="{ active: script === 'rashi' }" @click="emit('update:script', 'rashi')">רש"י</button>
        </div>
        <button class="close-btn" @click="emit('dismiss')"><IconDismiss20Regular /></button>
      </div>
      <textarea
        ref="textRef"
        class="popup-textarea"
        :value="result.text || ''"
        :placeholder="result.text ? '' : 'לא נמצא טקסט באזור הנבחר'"
        dir="rtl"
      />
      <div class="popup-actions">
        <button class="cancel-btn" @click="emit('dismiss')">ביטול</button>
        <button class="copy-btn" :disabled="!result.text" @click="copyText">
          <IconCopy20Regular />
          {{ copied ? '✓ הועתק' : 'העתק' }}
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.popup-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  z-index: 9999;
  display: flex;
  align-items: center;
  justify-content: center;
}
.popup {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 16px;
  width: min(560px, 90vw);
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.4);
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.popup-header {
  display: flex;
  align-items: center;
  gap: 8px;
}
.popup-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
  flex: 1;
}
.script-toggle {
  display: flex;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  overflow: hidden;
}
.script-btn {
  padding: 2px 10px;
  font-size: 11px;
  color: var(--text-secondary);
  background: none;
  border: none;
  cursor: pointer;
}
.script-btn.active {
  background: var(--accent-color);
  color: #fff;
}
.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  border-radius: 4px;
  color: var(--text-secondary);
}
.close-btn svg { width: 16px; height: 16px; }
.popup-textarea {
  width: 100%;
  min-height: 140px;
  padding: 10px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: var(--bg-primary);
  color: var(--text-primary);
  font-size: 14px;
  font-family: inherit;
  resize: vertical;
  direction: rtl;
  text-align: right;
  box-sizing: border-box;
  outline: none;
}
.popup-textarea:focus { border-color: var(--accent-color); }
.popup-actions {
  display: flex;
  gap: 8px;
  justify-content: flex-start;
}
.cancel-btn {
  padding: 6px 16px;
  font-size: 13px;
  border-radius: 4px;
  border: 1px solid var(--border-color);
  background: var(--bg-primary);
  color: var(--text-secondary);
}
.copy-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 16px;
  font-size: 13px;
  border-radius: 4px;
  background: var(--accent-color);
  color: #fff;
}
.copy-btn:disabled { opacity: 0.4; cursor: default; }
.copy-btn svg { width: 14px; height: 14px; }
</style>
