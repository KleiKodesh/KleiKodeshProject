<script setup lang="ts">
import {
  IconHighlight20Regular, IconHighlight20Filled,
  IconPen20Regular, IconPen20Filled,
  IconTextAdd20Regular, IconTextAdd20Filled,
  IconSignature20Regular, IconSignature20Filled,
  IconComment20Regular, IconComment20Filled,
  IconDismiss20Regular,
} from '@iconify-prerendered/vue-fluent'
import { useFloatingPanel } from '@/composables/useFloatingPanel'
import { ref } from 'vue'

// AnnotationEditorType values from pdf.mjs
const AnnotationEditorType = { NONE: 0, FREETEXT: 3, HIGHLIGHT: 9, INK: 15, SIGNATURE: 101, COMMENT: 102 }

const props = defineProps<{ iframeEl: HTMLIFrameElement | null }>()
const emit = defineEmits<{ close: [] }>()

const DEFAULT_POSITION = { x: window.innerWidth / 2 - 140, y: 80 }
const { panelRef, panelStyle } = useFloatingPanel({ initialPosition: DEFAULT_POSITION })

const activeMode = ref<number>(AnnotationEditorType.NONE)

function getEventBus(): any {
  return (props.iframeEl?.contentWindow as any)?.PDFViewerApplication?.eventBus ?? null
}

function setEditorMode(mode: number) {
  const bus = getEventBus()
  if (!bus) return
  const next = activeMode.value === mode ? AnnotationEditorType.NONE : mode
  bus.dispatch('switchannotationeditormode', { source: window, mode: next })
  activeMode.value = next
}
</script>

<template>
  <div ref="panelRef" class="editor-toolbar" :style="panelStyle" dir="rtl">
    <div class="drag-handle" />

    <button class="editor-btn" :class="{ active: activeMode === 9 }" title="הדגשה" @click="setEditorMode(9)">
      <IconHighlight20Filled v-if="activeMode === 9" /><IconHighlight20Regular v-else />
    </button>
    <button class="editor-btn" :class="{ active: activeMode === 15 }" title="ציור חופשי" @click="setEditorMode(15)">
      <IconPen20Filled v-if="activeMode === 15" /><IconPen20Regular v-else />
    </button>
    <button class="editor-btn" :class="{ active: activeMode === 3 }" title="הוספת טקסט" @click="setEditorMode(3)">
      <IconTextAdd20Filled v-if="activeMode === 3" /><IconTextAdd20Regular v-else />
    </button>
    <button class="editor-btn" :class="{ active: activeMode === 101 }" title="חתימה" @click="setEditorMode(101)">
      <IconSignature20Filled v-if="activeMode === 101" /><IconSignature20Regular v-else />
    </button>
    <button class="editor-btn" :class="{ active: activeMode === 102 }" title="הערה" @click="setEditorMode(102)">
      <IconComment20Filled v-if="activeMode === 102" /><IconComment20Regular v-else />
    </button>

    <div class="divider" />

    <button class="editor-btn" title="סגור" @click="emit('close')">
      <IconDismiss20Regular />
    </button>
  </div>
</template>

<style scoped>
.editor-toolbar {
  position: fixed;
  z-index: 9999;
  display: flex;
  align-items: center;
  gap: 2px;
  padding: 1px 3px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.4), 0 1px 3px rgba(0, 0, 0, 0.25);
  user-select: none;
  touch-action: none;
  cursor: grab;
}

.editor-toolbar:active { cursor: grabbing; }

.drag-handle {
  width: 10px;
  height: 16px;
  background-image: radial-gradient(circle, var(--text-secondary) 1px, transparent 1px);
  background-size: 4px 4px;
  opacity: 0.4;
  flex-shrink: 0;
  margin-inline-end: 2px;
}

.editor-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  border-radius: 4px;
  flex-shrink: 0;
  color: var(--text-secondary);
}

.editor-btn svg { width: 16px; height: 16px; }
.editor-btn:hover { background: color-mix(in srgb, var(--text-primary) 6%, transparent); }
.editor-btn.active { color: var(--accent-color); background: color-mix(in srgb, var(--accent-color) 12%, transparent); }

.divider { width: 1px; height: 16px; background: var(--border-color); flex-shrink: 0; margin: 0 2px; }
</style>
