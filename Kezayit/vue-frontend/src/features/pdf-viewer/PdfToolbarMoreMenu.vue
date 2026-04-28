<script setup lang="ts">
import {
  IconChevronUp20Regular,
  IconChevronDown20Regular,
  IconArrowDownload20Regular,
  IconCrop20Regular,
  IconPrint20Regular,
  IconSlideLayout20Regular,
  IconArrowRotateClockwise20Regular,
  IconArrowRotateCounterclockwise20Regular,
  IconCursorClick20Regular,
  IconHandLeft20Regular,
  IconDocumentBulletList20Regular,
  IconArrowAutofitContent20Regular,
  IconArrowAutofitHeight20Regular,
  IconArrowAutofitWidth20Regular,
  IconAlignStretchVertical20Regular,
  IconAlignStretchHorizontal20Regular,
  IconApps20Regular,
  IconRectangleLandscape20Regular,
  IconDocumentMultiple20Regular,
  IconSquareMultiple20Regular,
} from '@iconify-prerendered/vue-fluent'
import { ZOOM_OPTIONS } from './usePdfViewerControls'

const ZOOM_ICONS: Record<string, unknown> = {
  'auto': IconArrowAutofitContent20Regular,
  'page-fit': IconArrowAutofitHeight20Regular,
  'page-width': IconArrowAutofitWidth20Regular,
}

const props = defineProps<{
  currentZoom: string
  currentPage: number
  totalPages: number
  showZoomSelectInline: boolean
  showPageTotalInline: boolean
  cursorTool: 'select' | 'hand'
  scrollMode: 'vertical' | 'horizontal' | 'wrapped' | 'page'
  spreadMode: 'none' | 'odd' | 'even'
}>()

const emit = defineEmits<{
  setZoom: [value: string]
  download: []
  print: []
  presentationMode: []
  firstPage: []
  lastPage: []
  rotateCw: []
  rotateCcw: []
  setCursorTool: [tool: 'select' | 'hand']
  setScrollMode: [mode: 'vertical' | 'horizontal' | 'wrapped' | 'page']
  setSpreadMode: [mode: 'none' | 'odd' | 'even']
  rectangleSelect: []
  documentProperties: []
}>()
</script>

<template>
  <div class="more-menu" dir="rtl">
    <template v-if="!props.showZoomSelectInline">
      <div class="section-label">זום</div>
      <button
        v-for="option in ZOOM_OPTIONS"
        :key="option.value"
        class="item"
        :class="{ active: props.currentZoom === option.value }"
        @click="emit('setZoom', option.value)"
      >
        <component :is="ZOOM_ICONS[option.value]" />
        {{ option.label }}
      </button>
      <div class="divider" />
    </template>

    <template v-if="!props.showPageTotalInline && props.totalPages">
      <div class="section-label">עמוד {{ props.currentPage }} מתוך {{ props.totalPages }}</div>
      <div class="divider" />
    </template>

    <div class="section-label">ניווט</div>
    <button class="item" @click="emit('firstPage')"><IconChevronUp20Regular />עמוד ראשון</button>
    <button class="item" @click="emit('lastPage')"><IconChevronDown20Regular />עמוד אחרון</button>
    <div class="divider" />

    <div class="section-label">קובץ</div>
    <button class="item" @click="emit('download')"><IconArrowDownload20Regular />הורדה</button>
    <button class="item" @click="emit('print')"><IconPrint20Regular />הדפסה</button>
    <button class="item" @click="emit('presentationMode')"><IconSlideLayout20Regular />מצג שקופיות</button>
    <div class="divider" />

    <div class="section-label">סיבוב</div>
    <button class="item" @click="emit('rotateCw')"><IconArrowRotateClockwise20Regular />סיבוב עם השעון</button>
    <button class="item" @click="emit('rotateCcw')"><IconArrowRotateCounterclockwise20Regular />סיבוב נגד השעון</button>
    <div class="divider" />

    <div class="section-label">כלי סמן</div>
    <button class="item" :class="{ active: props.cursorTool === 'select' }" @click="emit('setCursorTool', 'select')"><IconCursorClick20Regular />בחירת טקסט</button>
    <button class="item" :class="{ active: props.cursorTool === 'hand' }" @click="emit('setCursorTool', 'hand')"><IconHandLeft20Regular />כלי יד</button>
    <div class="divider" />

    <div class="section-label">מצב גלילה</div>
    <button class="item" :class="{ active: props.scrollMode === 'vertical' }" @click="emit('setScrollMode', 'vertical')"><IconAlignStretchVertical20Regular />אנכי</button>
    <button class="item" :class="{ active: props.scrollMode === 'horizontal' }" @click="emit('setScrollMode', 'horizontal')"><IconAlignStretchHorizontal20Regular />אופקי</button>
    <button class="item" :class="{ active: props.scrollMode === 'wrapped' }" @click="emit('setScrollMode', 'wrapped')"><IconApps20Regular />עטיפה</button>
    <button class="item" :class="{ active: props.scrollMode === 'page' }" @click="emit('setScrollMode', 'page')"><IconRectangleLandscape20Regular />עמוד בודד</button>
    <div class="divider" />

    <div class="section-label">פריסת עמודים</div>
    <button class="item" :class="{ active: props.spreadMode === 'none' }" @click="emit('setSpreadMode', 'none')"><IconRectangleLandscape20Regular />ללא</button>
    <button class="item" :class="{ active: props.spreadMode === 'odd' }" @click="emit('setSpreadMode', 'odd')"><IconDocumentMultiple20Regular />פריסה — עמוד ראשון בודד</button>
    <button class="item" :class="{ active: props.spreadMode === 'even' }" @click="emit('setSpreadMode', 'even')"><IconSquareMultiple20Regular />פריסה — עמוד ראשון זוגי</button>
    <div class="divider" />

    <div class="section-label">כלים</div>
    <button class="item" @click="emit('rectangleSelect')"><IconCrop20Regular />בחירת אזור</button>
    <button class="item" @click="emit('documentProperties')"><IconDocumentBulletList20Regular />מאפייני מסמך</button>
  </div>
</template>

<style scoped>
.more-menu {
  position: absolute;
  top: 100%;
  inset-inline-start: 0;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25);
  z-index: 9999;
  min-width: 200px;
  max-height: 70vh;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

.item {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  height: 36px;
  padding: 0 12px;
  font-size: 13px;
  color: var(--text-primary);
  border-radius: 0;
}

.item svg {
  width: 16px;
  height: 16px;
  color: var(--text-secondary);
  flex-shrink: 0;
}

.item:hover { background: color-mix(in srgb, var(--text-primary) 6%, transparent); }
.item.active { color: var(--accent-color); }
.item.active svg { color: var(--accent-color); }

.section-label {
  padding: 6px 12px 2px;
  font-size: 10px;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.divider {
  height: 1px;
  background: var(--border-color);
  margin: 4px 0;
}
</style>
