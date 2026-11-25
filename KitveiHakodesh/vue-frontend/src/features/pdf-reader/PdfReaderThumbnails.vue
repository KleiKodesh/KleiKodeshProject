<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, shallowReactive, watch } from 'vue'
import { useIntersectionObserver } from '@vueuse/core'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'

/**
 * Page thumbnails: one row per page, rendered (PDF.js page.render to a small canvas, at the
 * view's rotation) only once the row scrolls into the panel, and kept as an image after.
 * The current page is marked and kept in view; click or Enter goes there, Up / Down move.
 */
const props = defineProps<{
  document: PDFDocumentProxy
  pageNumber: number
  rotation: number
  /** A page's label for display (page labels when the document has them). */
  labelOf?: (n: number) => string
}>()
const emit = defineEmits<{ select: [pageNumber: number] }>()

const hostWindow = useHostWindow()
const THUMB_WIDTH = 120

const listRef = ref<HTMLElement | null>(null)
const pages = computed(() => Array.from({ length: props.document.numPages }, (_, i) => i + 1))
/** Rendered images by page, for the current rotation. */
const images = shallowReactive(new Map<number, string>())
/** Width/height of each page at the current rotation once known; unknown pages use the first page's. */
const aspects = shallowReactive(new Map<number, number>())
const defaultAspect = ref(1 / Math.SQRT2)
const visible = new Set<number>()
const rendering = new Set<number>()
let disposed = false

async function render(n: number) {
  if (images.has(n) || rendering.has(n) || disposed) return
  rendering.add(n)
  const rotation = props.rotation
  const doc = props.document
  try {
    const page = await doc.getPage(n)
    const base = page.getViewport({ scale: 1, rotation: (page.rotate + rotation) % 360 })
    aspects.set(n, base.width / base.height)
    const dpr = hostWindow.devicePixelRatio || 1
    const viewport = page.getViewport({ scale: (THUMB_WIDTH * dpr) / base.width, rotation: (page.rotate + rotation) % 360 })
    const canvas = hostWindow.document.createElement('canvas')
    canvas.width = Math.ceil(viewport.width)
    canvas.height = Math.ceil(viewport.height)
    await page.render({ canvas, viewport, intent: 'display' }).promise
    if (disposed || rotation !== props.rotation || doc !== props.document) return
    images.set(n, canvas.toDataURL())
    canvas.width = canvas.height = 0
  } catch {
    // A page that fails to render keeps its blank frame.
  } finally {
    rendering.delete(n)
  }
}

// Rows report entering / leaving the panel; only those in it render.
const rowEls = new Map<number, HTMLElement>()
function setRow(n: number, el: Element | null) {
  if (el) rowEls.set(n, el as HTMLElement)
  else rowEls.delete(n)
}
const targets = ref<HTMLElement[]>([])
onMounted(async () => {
  await nextTick()
  targets.value = [...rowEls.values()]
  scrollCurrentIntoView('auto')
})
useIntersectionObserver(
  targets,
  (entries) => {
    for (const e of entries) {
      const n = Number((e.target as HTMLElement).dataset.page)
      if (e.isIntersecting) {
        visible.add(n)
        void render(n)
      } else visible.delete(n)
    }
  },
  { root: listRef, rootMargin: '200px 0px', window: hostWindow },
)

watch(() => [props.rotation, props.document] as const, () => {
  images.clear()
  aspects.clear()
  for (const n of visible) void render(n)
})
void props.document.getPage(1).then((p) => {
  const v = p.getViewport({ scale: 1, rotation: (p.rotate + props.rotation) % 360 })
  defaultAspect.value = v.width / v.height
}).catch(() => {})

function scrollCurrentIntoView(behavior: ScrollBehavior = 'smooth') {
  rowEls.get(props.pageNumber)?.scrollIntoView({ block: 'nearest', behavior })
}
watch(() => props.pageNumber, () => scrollCurrentIntoView())

function onKeydown(e: KeyboardEvent) {
  const step = e.key === 'ArrowDown' ? 1 : e.key === 'ArrowUp' ? -1 : e.key === 'Home' ? -Infinity : e.key === 'End' ? Infinity : 0
  if (!step) return
  e.preventDefault()
  const n = Math.min(pages.value.length, Math.max(1, props.pageNumber + step))
  emit('select', n)
}

onMounted(() => listRef.value?.focus({ preventScroll: true }))
onBeforeUnmount(() => { disposed = true })
</script>

<template>
  <div ref="listRef" class="thumbs" tabindex="0" @keydown="onKeydown">
    <button
      v-for="n in pages"
      :key="n"
      :ref="(el) => setRow(n, el as Element | null)"
      :data-page="n"
      type="button"
      class="thumb"
      :class="{ current: n === pageNumber }"
      :title="$t('pdfReader.panel.page', { n: labelOf?.(n) ?? n })"
      tabindex="-1"
      @click="emit('select', n)"
    >
      <span class="thumb-frame" :style="{ width: `${THUMB_WIDTH}px`, aspectRatio: String(aspects.get(n) ?? defaultAspect) }">
        <img v-if="images.get(n)" :src="images.get(n)" alt="" draggable="false" />
      </span>
      <span class="thumb-label">{{ labelOf?.(n) ?? n }}</span>
    </button>
  </div>
</template>

<style scoped>
.thumbs {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  padding: 10px 8px;
  outline: none;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
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
  flex-shrink: 0;
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
.thumb-frame img {
  display: block;
  width: 100%;
  height: 100%;
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
