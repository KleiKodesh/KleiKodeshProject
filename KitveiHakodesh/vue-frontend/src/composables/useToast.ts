import { ref } from 'vue'

export type ToastVariant = 'info' | 'success' | 'error'

export type ToastOptions = {
  /** Milliseconds before the banner auto-dismisses. Pass 0 to keep it until dismissed manually. */
  duration?: number
  /** Accent style of the banner. */
  variant?: ToastVariant
}

// Shared singleton state — a single bottom banner rendered once at the app root
// (see ToastBanner.vue) and driven from anywhere via showToast().
const message = ref<string | null>(null)
const variant = ref<ToastVariant>('info')

let timer: ReturnType<typeof setTimeout> | null = null

function clearTimer() {
  if (timer) {
    clearTimeout(timer)
    timer = null
  }
}

/** Show the global bottom banner. Replaces any banner currently visible. */
export function showToast(text: string, options: ToastOptions = {}) {
  const { duration = 3500, variant: v = 'info' } = options
  clearTimer()
  message.value = text
  variant.value = v
  if (duration > 0) {
    timer = setTimeout(() => {
      message.value = null
    }, duration)
  }
}

/** Hide the banner immediately. */
export function dismissToast() {
  clearTimer()
  message.value = null
}

/**
 * Composable accessor for the global toast banner.
 * `message`/`variant` are read by ToastBanner.vue; callers typically only need
 * `showToast` / `dismissToast`.
 */
export function useToast() {
  return { message, variant, showToast, dismissToast }
}
