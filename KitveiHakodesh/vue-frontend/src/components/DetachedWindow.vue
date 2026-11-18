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
import { onBeforeUnmount, onMounted, watch } from 'vue'
import { provideHostWindow } from '@/composables/detachedWindow/hostWindow'
import { openDetachedWindow } from '@/composables/detachedWindow/openDetachedWindow'

const props = defineProps<{
  /** Stable name: the host remembers the window's size and place under it. */
  name: string
  title: string
  width?: number
  height?: number
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

onBeforeUnmount(() => handle?.close())
</script>

<template>
  <Teleport v-if="handle" :to="handle.mountElement">
    <slot />
  </Teleport>
</template>
