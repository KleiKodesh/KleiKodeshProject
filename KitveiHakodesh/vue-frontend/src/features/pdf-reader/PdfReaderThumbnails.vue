<script setup lang="ts">
import { ref, shallowReactive, watch } from 'vue'
import { WindowedScroller, type WindowedScrollerApi } from 'vue-windowed-scroller'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import PdfThumbImage from './PdfThumbImage.vue'

/**
 * Page thumbnails: one WindowedScroller row per page, so only the rows near the view are in
 * the DOM and only they ask the shared thumbnail engine (pdfThumbnailEngine) for a picture.
 * Opens at the current page (its start row), marks it and keeps it in view; click or Enter
 * goes there, Up / Down / Home / End move.
 */
const props = defineProps<{
  document: PDFDocumentProxy
  pageNumber: number
  rotation: number
  /** A page's label for display (page labels when the document has them). */
  labelOf?: (n: number) => string
}>()
const emit = defineEmits<{ select: [pageNumber: number] }>()

const THUMB_WIDTH = 120

/** A new document is a new list (re-keyed: its start row is read once). */
const docIds = new WeakMap<PDFDocumentProxy, number>()
let nextDocId = 1
function docKey(doc: PDFDocumentProxy): number {
  let id = docIds.get(doc)
  if (!id) docIds.set(doc, (id = nextDocId++))
  return id
}

const scroller = ref<WindowedScrollerApi | null>(null)
/** The panel opens at the page being read (read once: the scroller's start row). */
const initialIndex = props.pageNumber - 1
/** Width / height of pages already drawn, at the current rotation; others take the first page's. */
const aspects = shallowReactive(new Map<number, number>())
const defaultAspect = ref(1 / Math.SQRT2)

watch(
  () => [props.document, props.rotation] as const,
  ([doc, rotation]) => {
    aspects.clear()
    void doc.getPage(1).then((p) => {
      if (doc !== props.document || rotation !== props.rotation) return
      const v = p.getViewport({ scale: 1, rotation: (p.rotate + rotation) % 360 })
      defaultAspect.value = v.width / v.height
    }).catch(() => {})
  },
  { immediate: true },
)

// The current page follows the reader: brought into view (nearest edge) by the scroller itself.
watch(() => props.pageNumber, (n) => scroller.value?.revealIndex(n - 1))

function onKeydown(e: KeyboardEvent) {
  const step = e.key === 'ArrowDown' ? 1 : e.key === 'ArrowUp' ? -1 : e.key === 'Home' ? -Infinity : e.key === 'End' ? Infinity : 0
  if (step) {
    e.preventDefault()
    emit('select', Math.min(props.document.numPages, Math.max(1, props.pageNumber + step)))
  } else if (e.key === 'Enter') {
    e.preventDefault()
    emit('select', props.pageNumber)
  }
}

function onOpened() {
  scroller.value?.scrollerElement?.focus({ preventScroll: true })
}
</script>

<template>
  <div class="thumbs-panel">
  <WindowedScroller
    :key="docKey(document)"
    ref="scroller"
    class="thumbs"
    thin
    tabindex="0"
    :count="document.numPages"
    :initial-index="initialIndex"
    :end-padding-em="0.6"
    @keydown="onKeydown"
    @opened="onOpened"
  >
    <template #row="{ index }">
      <div class="thumb-row" :class="{ first: index === 0 }">
        <button
          type="button"
          class="thumb"
          :class="{ current: index + 1 === pageNumber }"
          :title="$t('pdfReader.panel.page', { n: labelOf?.(index + 1) ?? index + 1 })"
          tabindex="-1"
          @click="emit('select', index + 1)"
        >
          <span class="thumb-frame" :style="{ width: `${THUMB_WIDTH}px`, aspectRatio: String(aspects.get(index) ?? defaultAspect) }">
            <PdfThumbImage :doc="document" :page-index="index" :rotation="rotation" :css-width="THUMB_WIDTH" @aspect="aspects.set(index, $event)" />
          </span>
          <span class="thumb-label">{{ labelOf?.(index + 1) ?? index + 1 }}</span>
        </button>
      </div>
    </template>
  </WindowedScroller>
  </div>
</template>

<style scoped>
.thumbs-panel {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
}
.thumbs-panel > .windowed-scroller {
  flex: 1;
  min-height: 0;
}
.thumbs {
  outline: none;
}
.thumb-row {
  display: flex;
  justify-content: center;
  padding: 4px 8px;
}
.thumb-row.first {
  padding-top: 10px;
}
.thumb {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 3px;
  height: auto;
  padding: 4px;
  border-radius: 4px;
  background: none;
}
.thumb:hover {
  background: var(--hover-bg);
}
.thumb-frame {
  display: block;
  background: #fff;
  box-shadow: 0 0 0 1px var(--border-color);
  overflow: hidden;
}
.thumb.current .thumb-frame {
  box-shadow: 0 0 0 2px var(--accent-color);
}
.thumbs:focus-visible .thumb.current {
  background: var(--hover-bg);
}
.thumb-label {
  font-size: 11px;
  color: var(--text-secondary);
}
.thumb.current .thumb-label {
  color: var(--accent-color);
}
</style>
