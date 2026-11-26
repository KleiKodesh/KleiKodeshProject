<script setup lang="ts">
/**
 * One page's picture in the organizer grid, from the document's shared thumbnail engine (the
 * viewer's panel draws from the same one). Mounted only while its grid row is (WindowedScroller),
 * so only pages near the view render. Blank pages are drawn as white.
 */
import { computed, ref } from 'vue'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import PdfThumbImage from '@/features/pdf-reader/PdfThumbImage.vue'

const props = defineProps<{
  doc: PDFDocumentProxy | null
  /** -1 = blank page. */
  pageIndex: number
  rotation: number
  width: number
  /** Width / height of the blank page (turn included). */
  blankRatio?: number
}>()

/** The box every card has: the page fits inside it. */
const boxHeight = computed(() => props.width * 1.3)
const aspect = ref(0.77)
const frame = computed(() => {
  const a = aspect.value
  return a >= props.width / boxHeight.value
    ? { width: props.width, height: props.width / a }
    : { width: boxHeight.value * a, height: boxHeight.value }
})
</script>

<template>
  <div class="thumb" :style="{ width: `${width}px`, height: `${boxHeight}px` }">
    <div v-if="pageIndex < 0 || !doc" class="blank" :style="{ aspectRatio: String(blankRatio ?? 0.77) }" />
    <div v-else class="frame" :style="{ width: `${frame.width}px`, height: `${frame.height}px` }">
      <PdfThumbImage :doc="doc" :page-index="pageIndex" :rotation="rotation" :css-width="width" @aspect="aspect = $event" />
    </div>
  </div>
</template>

<style scoped>
.thumb {
  display: flex;
  align-items: center;
  justify-content: center;
}
.frame,
.blank {
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.25);
  background: #fff;
}
.blank {
  width: 100%;
  max-height: 100%;
}
</style>
