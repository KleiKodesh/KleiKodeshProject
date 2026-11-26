<script setup lang="ts">
import {
  IconTextAdd20Regular,
  IconHighlight20Regular,
  IconPen20Regular,
  IconImage20Regular,
  IconSignature20Regular,
  IconAdd16Regular,
  IconArrowUndo20Regular,
  IconArrowRedo20Regular,
  IconDelete20Regular,
  IconDismiss20Regular,
} from '@iconify-prerendered/vue-fluent'
import { IconReOrderDotsVertical20Regular } from '@iconify-prerendered/vue-fluent'
import { nextTick, ref, watch, type Component } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useFloatingBar } from './useFloatingBar'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { AnnotationEditorType, HIGHLIGHT_COLORS, type PdfEditing } from './usePdfEditing'
import { PDF_KEYS, withKey } from './usePdfReaderShortcuts'

/** The floating, horizontal editing bar over the pages: the editors, the active editor's settings, undo / redo / delete. Dragged by its grip or background. */
const props = defineProps<{ editing: PdfEditing }>()
const emit = defineEmits<{ close: [] }>()
const T = AnnotationEditorType
const e = props.editing

// Drag: anywhere in the app window (shared with the OCR bar: useFloatingBar).
const bar = ref<HTMLElement | null>(null)
const anchor = ref<HTMLElement | null>(null)
const { hostWindow, pos, dragHandlers } = useFloatingBar(bar, anchor)

// PDF.js's editor order: highlight, text, draw, image.
const TOOLS: { mode: number; icon: Component; title: string; key: string }[] = [
  { mode: T.HIGHLIGHT, icon: IconHighlight20Regular, title: 'pdfReader.edit.highlight', key: PDF_KEYS.editTool.highlight },
  { mode: T.FREETEXT, icon: IconTextAdd20Regular, title: 'pdfReader.edit.text', key: PDF_KEYS.editTool.text },
  { mode: T.INK, icon: IconPen20Regular, title: 'pdfReader.edit.draw', key: PDF_KEYS.editTool.draw },
  { mode: T.STAMP, icon: IconImage20Regular, title: 'pdfReader.edit.addImage', key: PDF_KEYS.editTool.image },
  { mode: T.SIGNATURE, icon: IconSignature20Regular, title: 'pdfReader.edit.signature.tool', key: PDF_KEYS.editTool.signature },
]
/** Whether a tool's button shows as on. */
const isOn = (mode: number) => e.mode.value === mode
// Picking a tool opens its panel; picking it again closes both (as PDF.js).
const panelOpen = ref(false)
function pickTool(mode: number) {
  // The image tool has no settings: its button picks a picture and places it straight away.
  if (mode === T.STAMP) {
    panelOpen.value = false
    return e.addImage()
  }
  // The signature panel (new / saved signatures) lists what is saved.
  if (mode === T.SIGNATURE) void e.managers.loadSignatures()
  panelOpen.value = e.mode.value !== mode
  e.setMode(mode)
}

// The panel always opens on screen: below the bar, or above when there is no room below;
// aligned to its button, shifted back inside the window at the sides.
// (a function ref: the panel sits in a v-for, where a plain ref would be an array)
const panel = ref<HTMLElement | null>(null)
const panelStyle = ref<Record<string, string>>({ visibility: 'hidden' })
async function placePanel() {
  await nextTick()
  const el = panel.value
  const button = el?.parentElement?.querySelector('button')
  if (!el || !button || !bar.value) return
  const b = button.getBoundingClientRect()
  const r = bar.value.getBoundingClientRect()
  const w = el.offsetWidth
  const h = el.offsetHeight
  const vw = hostWindow.innerWidth
  const vh = hostWindow.innerHeight
  const gap = 6
  const top = r.bottom + gap + h <= vh || r.top - gap - h < 0 ? Math.min(r.bottom + gap, vh - h) : r.top - gap - h
  const rtl = getComputedStyle(bar.value).direction === 'rtl'
  const left = Math.min(Math.max(0, rtl ? b.right - w : b.left), vw - w)
  panelStyle.value = { top: `${Math.max(0, top)}px`, left: `${left}px` }
}
watch([panelOpen, () => e.mode.value, pos], () => {
  panelStyle.value = { visibility: 'hidden' }
  if (panelOpen.value) void placePanel()
}, { deep: true })
useEventListener(hostWindow, 'resize', () => panelOpen.value && placePanel())
// The app's dropdown closing (click outside, window blur); clicks on the bar itself are the tools' own.
useDropdownClose(panel, () => (panelOpen.value = false), { ignore: [bar], enabled: panelOpen, fitToViewport: false })

const num = (ev: Event) => Number((ev.target as HTMLInputElement).value)
const str = (ev: Event) => (ev.target as HTMLInputElement).value

</script>

<template>
  <span ref="anchor" class="edit-anchor" />
  <Teleport :to="hostWindow.document.body">
  <div
    ref="bar"
    class="pdf-edit-toolbar"
    dir="rtl"
    :style="{ left: `${pos.x}px`, top: `${pos.y}px` }"
    v-on="dragHandlers"
  >
    <span class="grip" :title="$t('bookView.toolbar.dragHint')"><IconReOrderDotsVertical20Regular /></span>
    <!-- As PDF.js's editor buttons: a button picks the editor; its settings open in a panel under it. -->
    <span v-for="tool in TOOLS" :key="tool.mode" class="tool">
      <button
        :class="{ active: isOn(tool.mode) }"
        :aria-pressed="isOn(tool.mode)"
        :aria-expanded="isOn(tool.mode) && panelOpen"
        :title="withKey($t(tool.title), tool.key)"
        @click="pickTool(tool.mode)"
      >
        <component :is="tool.icon" />
      </button>
      <div v-if="isOn(tool.mode) && panelOpen" :ref="(el) => (panel = el as HTMLElement | null)" class="params-panel" :style="panelStyle" @pointerdown.stop>
        <template v-if="tool.mode === T.FREETEXT">
          <div class="param-row">
            <span class="param-label">{{ $t('pdfReader.edit.color') }}</span>
            <input type="color" class="param-color" :value="e.params.freeTextColor" @input="e.setFreeTextColor(str($event))" />
          </div>
          <div class="param-row">
            <span class="param-label">{{ $t('pdfReader.edit.size') }}</span>
            <input type="range" min="5" max="100" step="1" :value="e.params.freeTextSize" @input="e.setFreeTextSize(num($event))" />
          </div>
        </template>
        <template v-else-if="tool.mode === T.HIGHLIGHT">
          <div class="param-row">
            <span class="param-label">{{ $t('pdfReader.edit.highlightColor') }}</span>
            <div class="swatches">
              <button
                v-for="(hex, name) in HIGHLIGHT_COLORS"
                :key="name"
                class="swatch"
                :class="{ chosen: e.params.highlightColor.toLowerCase() === hex.toLowerCase() }"
                :style="{ '--swatch': hex }"
                :title="$t(`pdfReader.edit.colors.${name}`)"
                @click="e.setHighlightColor(hex)"
              />
            </div>
          </div>
          <div class="param-row">
            <span class="param-label">{{ $t('pdfReader.edit.thickness') }}</span>
            <input type="range" min="8" max="24" step="1" :value="e.params.highlightThickness" @input="e.setHighlightThickness(num($event))" />
          </div>
          <label class="param-row param-switch">
            <span class="param-label">{{ $t('pdfReader.edit.showAll') }}</span>
            <input type="checkbox" role="switch" :checked="e.params.highlightShowAll" @change="e.setHighlightShowAll(($event.target as HTMLInputElement).checked)" />
          </label>
        </template>
        <template v-else-if="tool.mode === T.SIGNATURE">
          <!-- As PDF.js's signature panel: saved signatures to place, and a new one. -->
          <div v-if="e.managers.saved.value.length" class="param-row">
            <span class="param-label">{{ $t('pdfReader.edit.signature.saved') }}</span>
            <div v-for="sig in e.managers.saved.value" :key="sig.uuid" class="saved-sig">
              <button class="saved-sig-place" :title="sig.description" @click="e.managers.placeSignature(sig)">
                <svg :viewBox="sig.viewBox" preserveAspectRatio="xMidYMid meet" :class="{ contours: sig.lines.areContours }"><path :d="sig.path" /></svg>
                <span class="saved-sig-desc">{{ sig.description }}</span>
              </button>
              <button class="saved-sig-delete" :title="$t('pdfReader.edit.signature.deleteSaved')" @click="e.managers.deleteSignature(sig.uuid)"><IconDelete20Regular /></button>
            </div>
          </div>
          <button class="param-action new-sig" @click="e.newSignature"><IconAdd16Regular />{{ $t('pdfReader.edit.signature.new') }}</button>
        </template>
        <template v-else-if="tool.mode === T.INK">
          <div class="param-row">
            <span class="param-label">{{ $t('pdfReader.edit.color') }}</span>
            <input type="color" class="param-color" :value="e.params.inkColor" @input="e.setInkColor(str($event))" />
          </div>
          <div class="param-row">
            <span class="param-label">{{ $t('pdfReader.edit.thickness') }}</span>
            <input type="range" min="1" max="20" step="1" :value="e.params.inkThickness" @input="e.setInkThickness(num($event))" />
          </div>
          <div class="param-row">
            <span class="param-label">{{ $t('pdfReader.edit.opacity') }}</span>
            <input type="range" min="0.05" max="1" step="0.05" :value="e.params.inkOpacity" @input="e.setInkOpacity(num($event))" />
          </div>
        </template>
      </div>
    </span>

    <span class="sep" />

    <button :disabled="!e.states.hasSomethingToUndo" :title="$t('pdfReader.edit.undo')" @click="e.undo">
      <IconArrowUndo20Regular />
    </button>
    <button :disabled="!e.states.hasSomethingToRedo" :title="$t('pdfReader.edit.redo')" @click="e.redo">
      <IconArrowRedo20Regular />
    </button>
    <button :disabled="!e.states.hasSelectedEditor" :title="$t('pdfReader.edit.delete')" @click="e.deleteSelected">
      <IconDelete20Regular />
    </button>
    <span class="sep" />
    <!-- close the editing bar, as the OCR bar's X -->
    <button class="close-btn" :title="$t('pdfViewer.closeEsc')" @click="emit('close')">
      <IconDismiss20Regular />
    </button>
  </div>
  </Teleport>
</template>

<style scoped>
.edit-anchor {
  display: none;
}
.pdf-edit-toolbar {
  /* small buttons: a compact floating palette */
  --toolbar-button-size: 26px;
  position: fixed;
  z-index: 9000;
  display: flex;
  flex-direction: row;
  align-items: center;
  gap: 1px;
  padding: 2px;
  background: var(--bg-toolbar);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.18);
  cursor: grab;
  touch-action: none;
  user-select: none;
}
.grip {
  display: flex;
  align-items: center;
  height: var(--toolbar-button-size);
  opacity: 0.6;
  cursor: grab;
}
.grip svg {
  width: 14px;
  height: 14px;
}
button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--toolbar-button-size);
  height: var(--toolbar-button-size);
  padding: 4px;
  border-radius: 4px;
  flex-shrink: 0;
}
button svg {
  width: 14px;
  height: 14px;
}
button.active {
  color: var(--accent-color);
}
button:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
.sep {
  width: 1px;
  height: 16px;
  margin: 0 3px;
  background: var(--border-color);
  flex-shrink: 0;
}
.field {
  display: flex;
  align-items: center;
  gap: 4px;
  margin: 0 4px;
  white-space: nowrap;
  cursor: default;
  font-size: 12px;
  white-space: nowrap;
}
.field span {
  font-size: 11px;
}
.field input[type='range'] {
  width: 80px;
}
.field input[type='color'] {
  width: 22px;
  height: 22px;
  padding: 0;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: none;
}
.swatch {
  width: 18px;
  height: 18px;
  padding: 0;
  margin: 0 2px;
  border-radius: 50%;
  background: var(--swatch);
  border: 1px solid var(--border-color);
}
.swatch.chosen {
  outline: 2px solid var(--accent-color);
  outline-offset: 1px;
}
.hint {
  font-size: 12px;
  opacity: 0.75;
  white-space: nowrap;
}

/* The settings panel under a tool button: PDF.js's editorParamsToolbar layout in the app's look. */
.tool {
  position: relative;
  display: flex;
}
.params-panel {
  /* placed by placePanel (fixed, in the window): always fully on screen */
  position: fixed;
  z-index: 1;
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 200px;
  padding: 12px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.18);
  cursor: default;
  font-size: 12px;
  color: var(--text-primary);
}
.param-row {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.param-row input[type='range'] {
  width: 100%;
}
.param-label {
  font-weight: 600;
}
.param-color {
  width: 100%;
  height: 24px;
  padding: 0;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: none;
}
.swatches {
  display: flex;
  gap: 6px;
}
.param-switch {
  flex-direction: row;
  justify-content: space-between;
  align-items: center;
}
.param-action {
  width: 100%;
  height: 30px;
  padding: 0 12px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: var(--control-bg);
  color: var(--text-primary);
}
.new-sig {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  width: 100%;
}
.saved-sig {
  display: flex;
  align-items: stretch;
  gap: 2px;
}
.saved-sig-place {
  flex: 1;
  width: auto;
  height: 44px;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px 6px;
  border: 1px solid var(--border-color);
  background: #fff;
  color: #000;
}
.saved-sig-place svg {
  width: 72px;
  height: 34px;
}
.saved-sig-place path {
  fill: none;
  stroke: #000;
  stroke-width: 1.5px;
  vector-effect: non-scaling-stroke;
}
.saved-sig-place svg.contours path {
  fill: #000;
  stroke: none;
}
.saved-sig-desc {
  max-width: 110px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 11px;
}
.saved-sig-delete {
  height: 44px;
}
.param-action:hover {
  background: var(--control-bg-hover);
}
</style>
