<script setup lang="ts">
import { WIDGET_WINDOW_SIZE } from '@/features/widgets/widgetWindowStore'
/**
 * Renders its slot in a separate window instead of in place.
 *
 * The window opens when this component is created and closes when it is removed;
 * `closed` is emitted when the user closes the window themselves, and `blocked`
 * when it could not open at all. The slot's components keep working there as long
 * as they take their window from useHostWindow (see hostWindow.ts).
 *
 * Create it from a click: browsers only allow a popup as the result of one.
 */
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { provideHostWindow } from '@/composables/detachedWindow/hostWindow'
import { openDetachedWindow } from '@/composables/detachedWindow/openDetachedWindow'
import ToastBanner from '@/components/ToastBanner.vue'

const props = defineProps<{
  /** Stable name: the host remembers the window's size and place under it. */
  name: string
  title: string
  width?: number
  height?: number
  /**
   * Size the window to its content, and keep it so as the content changes. The slot is
   * then laid out at its natural size (not stretched to the window) and measured.
   */
  fitContent?: boolean
}>()

const emit = defineEmits<{ closed: []; blocked: [] }>()

const handle = openDetachedWindow(
  { name: props.name, title: props.title, width: props.width ?? WIDGET_WINDOW_SIZE.width, height: props.height ?? WIDGET_WINDOW_SIZE.height },
  () => emit('closed'),
)

if (handle) provideHostWindow(handle.window)

onMounted(() => {
  if (!handle) emit('blocked')
})

watch(
  () => props.title,
  (title) => handle?.setTitle(title),
)

// ── Fit to content ──────────────────────────────────────────────────────────
const fitElement = ref<HTMLElement | null>(null)
let fitObserver: ResizeObserver | null = null
watch(
  [fitElement, () => props.fitContent],
  ([element, fit]) => {
    fitObserver?.disconnect()
    fitObserver = null
    if (!element || !fit || !handle) return
    // The popup's own observer: one from this window never fires for its elements.
    fitObserver = new handle.window.ResizeObserver(() => {
      const box = element.getBoundingClientRect()
      handle.resizeContent(box.width, box.height)
    })
    fitObserver.observe(element)
  },
  { flush: 'post' },
)

onBeforeUnmount(() => {
  fitObserver?.disconnect()
  handle?.close()
})

/** Brings the window to the front. */
defineExpose({ focus: () => handle?.window.focus() })
</script>

<template>
  <Teleport v-if="handle" :to="handle.mountElement">
    <div v-if="fitContent" ref="fitElement" class="detached-fit">
      <slot />
    </div>
    <slot v-else />
    <ToastBanner />
  </Teleport>
</template>

<style scoped>
/* Laid out at its natural size, which the window then takes. */
.detached-fit {
  width: max-content;
  min-width: 220px;
  max-width: 100%;
  display: flex;
  flex-direction: column;
}
</style>
