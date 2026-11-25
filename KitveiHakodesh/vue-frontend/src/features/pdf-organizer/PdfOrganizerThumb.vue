<script setup lang="ts">
/** One page's picture in the organizer grid: drawn once it scrolls near view, blank pages drawn as white. */
import { ref, watch } from 'vue'
import { useIntersectionObserver } from '@vueuse/core'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import type { ThumbnailCache } from './thumbnailCache'

const props = defineProps<{
  cache: ThumbnailCache
  doc: PDFDocumentProxy | null
  /** -1 = blank page. */
  pageIndex: number
  rotation: number
  width: number
  /** Width / height of the blank page (turn included). */
  blankRatio?: number
}>()

const hostWindow = useHostWindow()
const el = ref<HTMLElement | null>(null)
const visible = ref(false)
const url = ref<string | null>(null)
const failed = ref(false)

useIntersectionObserver(el, ([entry]) => { if (entry?.isIntersecting) visible.value = true }, { window: hostWindow, rootMargin: '400px' })

let request = 0
watch(
  () => [visible.value, props.doc, props.pageIndex, props.rotation, props.width] as const,
  async ([isVisible, doc, pageIndex, rotation, width]) => {
    if (!isVisible || !doc || pageIndex < 0) return
    const mine = ++request
    try {
      const u = await props.cache.get(doc, pageIndex, rotation, width)
      if (mine === request) { url.value = u; failed.value = false }
    } catch {
      if (mine === request) failed.value = true
    }
  },
  { immediate: true },
)
</script>

<template>
  <div ref="el" class="thumb" :style="{ width: `${width}px`, height: `${width * 1.3}px` }">
    <div v-if="pageIndex < 0" class="blank" :style="{ aspectRatio: String(blankRatio ?? 0.77) }" />
    <img v-else-if="url" :src="url" alt="" draggable="false" />
    <div v-else class="placeholder" :class="{ failed }" />
  </div>
</template>

<style scoped>
.thumb {
  display: flex;
  align-items: center;
  justify-content: center;
}
img,
.blank {
  max-width: 100%;
  max-height: 100%;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.25);
  background: #fff;
}
.blank {
  width: 100%;
  max-height: 100%;
}
.placeholder {
  width: 77%;
  height: 100%;
  background: var(--bg-secondary);
  border-radius: 2px;
}
.placeholder.failed {
  outline: 1px dashed var(--border-color);
}
</style>
