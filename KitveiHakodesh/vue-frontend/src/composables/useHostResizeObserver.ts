import { computed, ref, watch } from 'vue'
import { useEventListener, tryOnMounted, tryOnScopeDispose, unrefElement, type MaybeComputedElementRef } from '@vueuse/core'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'

/**
 * VueUse's useResizeObserver / useElementSize, but the observer comes from the window the element
 * is DRAWN in, not from this page's global window.
 *
 * An app window is a popup this page draws into (detachedWindow). VueUse (10.x) always builds
 * `new ResizeObserver` from the page's own realm - its `window` option only gates support - and an
 * observer from another window reports a popup's element only while that other window is drawing
 * frames. In the desktop app the page's own window never shows, so sizes froze at their first value
 * (measured: the PDF toolbar kept its first width and the row ran past the edge). Every observer of
 * an element that can live in an app window goes through here.
 */
type Targets = MaybeComputedElementRef | MaybeComputedElementRef[]

export function useHostResizeObserver(
  target: Targets,
  callback: ResizeObserverCallback,
  options: ResizeObserverOptions & { window?: unknown } = {},
) {
  const { window: _ignored, ...observerOptions } = options
  void _ignored
  const observers: ResizeObserver[] = []
  const cleanup = () => {
    for (const observer of observers) observer.disconnect()
    observers.length = 0
  }
  const elements = computed(() =>
    (Array.isArray(target) ? target : [target]).map((t) => unrefElement(t)).filter((e): e is HTMLElement | SVGElement => !!e),
  )
  const stopWatch = watch(
    elements,
    (els) => {
      cleanup()
      // one observer per window the elements are drawn in
      const byWindow = new Map<Window, Element[]>()
      for (const el of els) {
        const win = el.ownerDocument.defaultView
        if (!win || !('ResizeObserver' in win)) continue
        byWindow.set(win, [...(byWindow.get(win) ?? []), el])
      }
      for (const [win, list] of byWindow) {
        const observer = new (win as Window & typeof globalThis).ResizeObserver(callback)
        for (const el of list) observer.observe(el, observerOptions)
        observers.push(observer)
      }
    },
    { immediate: true, flush: 'post' },
  )
  const stop = () => {
    cleanup()
    stopWatch()
  }
  tryOnScopeDispose(stop)
  return { stop }
}

/** VueUse's useElementSize on useHostResizeObserver (same arguments and results). */
export function useHostElementSize(
  target: MaybeComputedElementRef,
  initialSize: { width: number; height: number } = { width: 0, height: 0 },
  options: { box?: 'content-box' | 'border-box' | 'device-pixel-content-box'; window?: unknown } = {},
) {
  const box = options.box ?? 'content-box'
  const width = ref(initialSize.width)
  const height = ref(initialSize.height)
  const { stop: stop1 } = useHostResizeObserver(
    target,
    ([entry]) => {
      if (!entry) return
      const el = entry.target
      if (el.namespaceURI?.includes('svg')) {
        const rect = el.getBoundingClientRect()
        width.value = rect.width
        height.value = rect.height
        return
      }
      const boxSize = box === 'border-box' ? entry.borderBoxSize : box === 'content-box' ? entry.contentBoxSize : entry.devicePixelContentBoxSize
      if (boxSize) {
        width.value = boxSize.reduce((sum, { inlineSize }) => sum + inlineSize, 0)
        height.value = boxSize.reduce((sum, { blockSize }) => sum + blockSize, 0)
      } else {
        width.value = entry.contentRect.width
        height.value = entry.contentRect.height
      }
    },
    { box },
  )
  tryOnMounted(() => {
    const el = unrefElement(target)
    if (el && 'offsetWidth' in el) {
      width.value = el.offsetWidth
      height.value = el.offsetHeight
    }
  })
  const stop2 = watch(
    () => unrefElement(target),
    (el) => {
      width.value = el ? initialSize.width : 0
      height.value = el ? initialSize.height : 0
    },
  )
  return {
    width,
    height,
    stop: () => {
      stop1()
      stop2()
    },
  }
}

/**
 * Keeps a VueUse useElementBounding current in an app window. Its own observer and its resize /
 * scroll listeners are the page window's; this re-reads it on the app window's resize
 * observations, and (unless `windowEvents` is false) on that window's resize and scroll.
 */
export function useHostBoundingUpdates(target: MaybeComputedElementRef, update: () => void, windowEvents = true) {
  if (windowEvents) {
    const hostWindow = useHostWindow()
    useEventListener(hostWindow, 'resize', update, { passive: true })
    useEventListener(hostWindow, 'scroll', update, { capture: true, passive: true })
  }
  return useHostResizeObserver(target, () => update())
}
