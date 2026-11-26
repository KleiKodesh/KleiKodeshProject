<script setup lang="ts">
/**
 * One page's thumbnail picture (pdfThumbnailEngine): asked for while this is mounted, let go
 * when it unmounts - in a WindowedScroller only the rows near the view are mounted, so only
 * they render, and a row scrolled past before its turn never does. Shared by the viewer's
 * thumbnails panel and the page organizer.
 */
import { onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { thumbnailEngineFor, thumbnailWidthFor, type ThumbnailHold, type ThumbnailImage } from './pdfThumbnailEngine'

const props = defineProps<{
  doc: PDFDocumentProxy
  pageIndex: number
  /** Turn added to the page's own (the view's rotation, or an organizer page's). */
  rotation: number
  /** Width shown, in CSS pixels. */
  cssWidth: number
}>()
const emit = defineEmits<{ aspect: [aspect: number] }>()

const hostWindow = useHostWindow()
const image = shallowRef<ThumbnailImage | null>(null)
const failed = ref(false)
let hold: ThumbnailHold | null = null
/** Showing in its scroller now: such rows render first (the engine's priority). */
const el = ref<HTMLElement | null>(null)
let showing = false
// The observer of the window this is drawn in (VueUse's would be the main window's, which in a
// detached window reports late or never while the main window is not being painted).
let observer: IntersectionObserver | null = null
onMounted(() => {
  const target = el.value
  const view = (target?.ownerDocument.defaultView ?? hostWindow) as typeof globalThis
  if (!target || !view.IntersectionObserver) return
  observer = new view.IntersectionObserver((entries) => {
    showing = entries[entries.length - 1]?.isIntersecting ?? false
    hold?.setShowing(showing)
  })
  observer.observe(target)
})

watch(
  () => [props.doc, props.pageIndex, props.rotation, thumbnailWidthFor(props.cssWidth, hostWindow.devicePixelRatio)] as const,
  ([doc, pageIndex, rotation, width]) => {
    hold?.release()
    const mine = (hold = thumbnailEngineFor(doc).request(pageIndex, rotation, width))
    failed.value = false
    image.value = mine.image
    mine.setShowing(showing)
    if (mine.image) return emit('aspect', mine.image.aspect)
    mine.promise.then(
      (img) => {
        if (hold !== mine) return
        image.value = img
        emit('aspect', img.aspect)
      },
      () => { if (hold === mine) failed.value = true },
    )
  },
  { immediate: true },
)
onBeforeUnmount(() => {
  observer?.disconnect()
  hold?.release()
  hold = null
})
</script>

<template>
  <span ref="el" class="thumb-pic">
    <img v-if="image" :src="image.url" alt="" draggable="false" />
    <span v-else class="thumb-pending" :class="{ failed }" />
  </span>
</template>

<style scoped>
.thumb-pic {
  display: block;
  width: 100%;
  height: 100%;
}
img {
  display: block;
  width: 100%;
  height: 100%;
}
.thumb-pending {
  display: block;
  width: 100%;
  height: 100%;
}
</style>
