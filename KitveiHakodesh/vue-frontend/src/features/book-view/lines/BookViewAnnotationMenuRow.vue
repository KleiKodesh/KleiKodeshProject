<script setup lang="ts">
import { ref } from 'vue'
import { IconEdit20Regular, IconEraser20Regular } from '@iconify-prerendered/vue-fluent'
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
      <IconEdit20Regular class="note-icon" />
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
        <IconEraser20Regular />
      </button>
    </div>
  </div>
</template>

<style scoped>
.annotation-menu-row {
  direction: rtl;
}

/* These rows sit inside ContextMenu.vue, which cannot style them (they belong to this
   child component) — so they take the menu's published row metrics from its CSS
   variables rather than restating the numbers. Do not inline literals here: the height
   and padding are load-bearing for hover-flushness and separator rasterization, and
   the reasoning lives in one place, on .context-menu-item. */
.note-row,
.highlight-row {
  display: flex;
  align-items: center;
  padding: var(--context-menu-row-padding-block) var(--context-menu-row-padding-inline);
  box-sizing: border-box;
  height: var(--context-menu-row-height);
}

.note-row {
  gap: 6px;
  cursor: pointer;
  font-size: 12px;
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

/* Same rule as ContextMenu.vue's .context-menu-separator, which cannot reach into this
   component. margin MUST stay 0 — margin around a separator is dead space no row's
   hover can paint, and that is what left a gap between the highlight and the rule. */
.separator {
  height: 1px;
  background: var(--border-color);
  margin-block: 0;
}

.highlight-row {
  gap: 5px;
  direction: rtl;
}

.highlight-label {
  font-size: 12px;
  color: var(--text-primary);
  margin-inline-end: 2px;
}

.color-swatch {
  width: 14px;
  height: 14px;
  border-radius: 3px;
  border: none;
  cursor: pointer;
  flex-shrink: 0;
  transition: transform 150ms;
}

.color-swatch:hover {
  transform: scale(1.15);
}

.color-swatch:active {
  transform: scale(1.05);
}

.clear-button {
  width: 22px;
  height: 22px;
  border-radius: 4px;
  border: none;
  background: none;
  cursor: pointer;
  color: var(--text-secondary);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  margin-inline-start: 1px;
}

.clear-button:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

.clear-button:active {
  transform: scale(0.98);
}
</style>
