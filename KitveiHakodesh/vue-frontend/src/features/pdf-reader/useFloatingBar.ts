import { onMounted, ref, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'

/**
 * A floating bar on the window's body (position: fixed), dragged anywhere in the app window
 * by its grip or its own background (never by its controls), kept fully on screen.
 * It starts at the top corner of the element that holds `anchor` (a hidden span left in place).
 */
export function useFloatingBar(bar: Ref<HTMLElement | null>, anchor: Ref<HTMLElement | null>, offset = { x: 12, y: 12 }) {
  const hostWindow = useHostWindow()
  const pos = ref({ x: offset.x, y: offset.y })

  function clamp() {
    const el = bar.value
    if (!el) return
    pos.value = {
      x: Math.min(Math.max(0, pos.value.x), hostWindow.innerWidth - el.offsetWidth),
      y: Math.min(Math.max(0, pos.value.y), hostWindow.innerHeight - el.offsetHeight),
    }
  }
  onMounted(() => {
    const r = (anchor.value?.parentElement ?? anchor.value)?.getBoundingClientRect()
    if (r) pos.value = { x: r.left + offset.x, y: r.top + offset.y }
  })
  useEventListener(hostWindow, 'resize', clamp)

  let drag: { dx: number; dy: number } | null = null
  function onPointerDown(ev: PointerEvent) {
    const t = ev.target as HTMLElement
    if (ev.button !== 0 || (t.closest('button, input, label, select') && !t.closest('.grip'))) return
    ev.preventDefault()
    drag = { dx: ev.clientX - pos.value.x, dy: ev.clientY - pos.value.y }
    bar.value?.setPointerCapture(ev.pointerId)
  }
  function onPointerMove(ev: PointerEvent) {
    if (!drag) return
    pos.value = { x: ev.clientX - drag.dx, y: ev.clientY - drag.dy }
    clamp()
  }
  const onPointerUp = () => (drag = null)
  // Clicking the bar's buttons leaves focus where it was (in the viewer), so the viewer's
  // shortcuts keep working; fields (colour, sliders, text) still take focus.
  function onMouseDown(ev: MouseEvent) {
    if (!(ev.target as HTMLElement).closest('input, select, textarea, [contenteditable="true"]')) ev.preventDefault()
  }

  /** Bind on the bar: v-on="dragHandlers". */
  const dragHandlers = { mousedown: onMouseDown, pointerdown: onPointerDown, pointermove: onPointerMove, pointerup: onPointerUp, pointercancel: onPointerUp }
  return { hostWindow, pos, dragHandlers }
}
