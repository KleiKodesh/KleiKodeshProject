<script setup lang="ts">
import { ref } from 'vue'
import { IconEdit24Regular, IconEraser24Regular } from '@iconify-prerendered/vue-fluent'
import { HIGHLIGHT_COLORS_LIST } from './bookViewAnnotationColors'
import AlertDialog from '@/components/AlertDialog.vue'

const props = defineProps<{
  onHighlight: (colorArgb: number) => void
  onClearHighlight: () => void
  onAddNote: () => void
}>()

const emit = defineEmits<{ close: [] }>()

const showNoSelectionAlert = ref(false)

function hasSelection(): boolean {
  const sel = window.getSelection()
  return !!sel && !sel.isCollapsed && (sel.toString().trim().length > 0)
}

function onColorClick(colorArgb: number) {
  if (!hasSelection()) { showNoSelectionAlert.value = true; return }
  props.onHighlight(colorArgb)
  emit('close')
}

function onClear() {
  if (!hasSelection()) { showNoSelectionAlert.value = true; return }
  props.onClearHighlight()
  emit('close')
}

function onNote() {
  if (!hasSelection()) { showNoSelectionAlert.value = true; return }
  props.onAddNote()
  emit('close')
}

function argbToCss(signedArgb: number): string {
  const unsigned = signedArgb >>> 0
  const r = (unsigned >>> 16) & 0xff
  const g = (unsigned >>> 8) & 0xff
  const b = unsigned & 0xff
  return `rgb(${r}, ${g}, ${b})`
}
</script>

<template>
  <div class="annotation-menu-row">
    <AlertDialog
      v-if="showNoSelectionAlert"
      message="יש לסמן טקסט תחילה"
      @close="showNoSelectionAlert = false"
    />
    <div class="note-row" @click="onNote">
      <IconEdit24Regular class="note-icon" />
      <span class="note-label">הוסף הערה</span>
    </div>
    <div class="separator" />
    <div class="highlight-row">
      <span class="highlight-label">סמן</span>
      <button
        v-for="colorArgb in HIGHLIGHT_COLORS_LIST"
        :key="colorArgb"
        class="color-swatch"
        :style="{ background: argbToCss(colorArgb) }"
        :aria-label="`סמן בצבע`"
        @click="onColorClick(colorArgb)"
      />
      <button class="clear-button" :aria-label="'הסר סימון'" @click="onClear">
        <IconEraser24Regular />
      </button>
    </div>
  </div>
</template>

<style scoped>
.annotation-menu-row {
  direction: rtl;
}

.note-row {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 16px;
  cursor: pointer;
  font-size: 13px;
}

.note-row:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

.note-row:active {
  background: color-mix(in srgb, var(--text-primary) 13%, transparent);
}

.note-icon {
  color: var(--text-secondary);
  flex-shrink: 0;
}

.note-label {
  color: var(--text-primary);
}

.separator {
  height: 1px;
  background: var(--border-color);
  margin-block: 2px;
}

.highlight-row {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 16px;
  direction: rtl;
}

.highlight-label {
  font-size: 13px;
  color: var(--text-primary);
  margin-inline-end: 2px;
}

.color-swatch {
  width: 16px;
  height: 16px;
  border-radius: 3px;
  border: none;
  cursor: pointer;
  flex-shrink: 0;
  transition: transform 150ms;
}

.color-swatch:hover {
  transform: scale(1.12);
}

.color-swatch:active {
  transform: scale(0.92);
}

.clear-button {
  width: 26px;
  height: 26px;
  border-radius: 4px;
  border: none;
  background: none;
  cursor: pointer;
  color: var(--text-secondary);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  margin-inline-start: 2px;
}

.clear-button:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

.clear-button:active {
  transform: scale(0.92);
}
</style>
