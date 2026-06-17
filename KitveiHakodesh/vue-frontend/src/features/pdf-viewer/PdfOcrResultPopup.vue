<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { IconDismiss20Regular, IconCopy20Regular, IconCheckmark20Regular, IconHourglassOneQuarter20Regular } from '@iconify-prerendered/vue-fluent'
import type { OcrSelectionResult, OcrScript } from './pdfViewerTypes'

const props = defineProps<{
  result: OcrSelectionResult
  script: OcrScript
  isProcessing?: boolean
  processingProgress?: number
}>()
const emit = defineEmits<{ dismiss: []; 'update:script': [OcrScript] }>()

const textRef = ref<HTMLTextAreaElement | null>(null)
const copied = ref(false)
const editableText = ref(props.result.text ?? '')

// Keep editableText in sync if the result changes (e.g. OCR finishes)
watch(() => props.result.text, (val) => {
  editableText.value = val ?? ''
})

const resultLabel = computed(() => {
  return props.result.isOcr ? 'טקסט מזוהה (OCR)' : 'טקסט נבחר'
})

const copyButtonLabel = computed(() => {
  return copied.value ? 'הועתק' : 'העתק'
})

async function copyText() {
  const text = editableText.value
  let success = false

  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text)
      success = true
    } catch {
      // Clipboard API blocked (e.g. WebView2 focus on iframe) — fall through to execCommand
    }
  }

  if (!success && textRef.value) {
    textRef.value.focus()
    textRef.value.select()
    success = document.execCommand('copy')
  }

  if (success) {
    copied.value = true
    setTimeout(() => { copied.value = false }, 1200)
  }
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
        <div class="header-left">
          <span class="result-badge" :class="{ 'is-ocr': props.result.isOcr }">
            {{ resultLabel }}
          </span>
          <div v-if="props.isProcessing" class="processing-indicator">
            <IconHourglassOneQuarter20Regular class="spinner" />
            <span class="processing-text">מעבד...</span>
          </div>
        </div>
        <button class="close-btn" @click="emit('dismiss')" title="סגור (Esc)" :disabled="props.isProcessing">
          <IconDismiss20Regular />
        </button>
      </div>

      <div class="popup-content">
        <textarea
          ref="textRef"
          class="popup-textarea"
          v-model="editableText"
          :placeholder="editableText ? '' : 'לא נמצא טקסט באזור הנבחר'"
          spellcheck="true"
          dir="rtl"
          :disabled="props.isProcessing"
        />
        <div v-if="props.isProcessing" class="progress-container">
          <div class="progress-bar">
            <div class="progress-fill" :style="{ width: ((props.processingProgress ?? 0) * 100) + '%' }" />
          </div>
          <span class="progress-text">{{ Math.round((props.processingProgress ?? 0) * 100) }}%</span>
        </div>
      </div>

      <div class="popup-footer">
        <button class="action-btn cancel-btn" @click="emit('dismiss')" :disabled="props.isProcessing">ביטול</button>
        <button 
          class="action-btn copy-btn" 
          :disabled="!editableText || props.isProcessing" 
          :class="{ copied: copied }"
          @click="copyText"
          title="העתק לחיתוך (Ctrl+C)"
        >
          <IconCheckmark20Regular v-if="copied" class="icon" />
          <IconCopy20Regular v-else class="icon" />
          <span>{{ copyButtonLabel }}</span>
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
  backdrop-filter: blur(2px);
  animation: fadeIn 150ms ease-out;
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

.popup {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  width: min(600px, 92vw);
  box-shadow: 0 12px 48px rgba(0, 0, 0, 0.5);
  display: flex;
  flex-direction: column;
  overflow: hidden;
  animation: slideUp 200ms cubic-bezier(0.16, 1, 0.3, 1);
}

@keyframes slideUp {
  from {
    opacity: 0;
    transform: translateY(20px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.popup-header {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 16px;
  border-bottom: 1px solid var(--border-color);
  background: var(--bg-primary);
}

.header-left {
  flex: 1;
  display: flex;
  align-items: center;
}

.result-badge {
  font-size: 12px;
  font-weight: 600;
  padding: 4px 10px;
  border-radius: 4px;
  background: color-mix(in srgb, var(--accent-color) 15%, transparent);
  color: var(--accent-color);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.result-badge.is-ocr {
  background: color-mix(in srgb, #f0a500 15%, transparent);
  color: #f0a500;
}

.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border-radius: 4px;
  color: var(--text-secondary);
  background: none;
  border: none;
  cursor: pointer;
  transition: all 100ms ease;
}

.close-btn:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
  color: var(--text-primary);
}

.close-btn svg {
  width: 16px;
  height: 16px;
}

.popup-content {
  flex: 1;
  min-height: 0;
  padding: 12px;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.popup-textarea {
  flex: 1;
  width: 100%;
  padding: 10px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: var(--bg-primary);
  color: var(--text-primary);
  font-size: 14px;
  font-family: inherit;
  resize: none;
  direction: rtl;
  text-align: right;
  box-sizing: border-box;
  outline: none;
  transition: border-color 100ms ease;
  min-height: 180px;
  overflow: auto;
}

.popup-textarea:focus {
  border-color: var(--accent-color);
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--accent-color) 20%, transparent);
}

.popup-textarea::placeholder {
  color: var(--text-secondary);
  opacity: 0.6;
}

.popup-footer {
  display: flex;
  gap: 8px;
  padding: 12px 16px;
  border-top: 1px solid var(--border-color);
  background: var(--bg-primary);
  justify-content: flex-end;
}

.action-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 16px;
  font-size: 13px;
  font-weight: 500;
  border-radius: 4px;
  border: none;
  cursor: pointer;
  transition: all 100ms ease;
}

.cancel-btn {
  border: 1px solid var(--border-color);
  background: var(--bg-toolbar);
  color: var(--text-primary);
}

.cancel-btn:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, var(--bg-toolbar));
}

.copy-btn {
  background: var(--accent-color);
  color: #fff;
  border: none;
}

.copy-btn:hover:not(:disabled) {
  background: color-mix(in srgb, var(--accent-color) 82%, #000);
}

.copy-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

.copy-btn.copied {
  background: #10b981;
  border: none;
  color: #fff;
  animation: pulse 300ms ease-out;
}

@keyframes pulse {
  0% { transform: scale(1); }
  50% { transform: scale(1.05); }
  100% { transform: scale(1); }
}

.copy-btn .icon {
  width: 14px;
  height: 14px;
  color: inherit;
}

.processing-indicator {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 10px;
  border-radius: 4px;
  background: color-mix(in srgb, #f0a500 15%, transparent);
  color: #f0a500;
  font-size: 12px;
  font-weight: 500;
}

.spinner {
  width: 14px;
  height: 14px;
  animation: spin 1s linear infinite;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

.processing-text {
  letter-spacing: 0.5px;
}

.progress-container {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 8px;
}

.progress-bar {
  flex: 1;
  height: 4px;
  background: color-mix(in srgb, var(--border-color) 50%, transparent);
  border-radius: 2px;
  overflow: hidden;
}

.progress-fill {
  height: 100%;
  background: linear-gradient(90deg, var(--accent-color), #10b981);
  transition: width 150ms ease;
  border-radius: 2px;
  box-shadow: 0 0 8px rgba(0, 120, 212, 0.4);
}

.progress-text {
  font-size: 11px;
  font-weight: 600;
  color: var(--text-secondary);
  min-width: 28px;
  text-align: right;
}

.close-btn:disabled,
.action-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>