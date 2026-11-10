import { computed, ref, watch } from 'vue'
import { useDocumentVisibility, useEventListener, useIntervalFn, useWindowSize } from '@vueuse/core'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { isWindowFullscreen } from '@/utils/isWindowFullscreen'

/** Tabs that count as "reading" for the 'reading' mode. */
const READING_ROUTES = new Set(['/book-view', '/fast-doc-view', '/pdf-view', '/html-view'])

/** What counts as the reader still being there. */
const ACTIVITY_EVENTS = ['keydown', 'pointerdown', 'wheel', 'scroll'] as const

/** How often idleness is re-checked and a refused lock is retried. */
const CHECK_INTERVAL_MS = 30_000

/**
 * Holds the screen awake (Screen Wake Lock API) per the keep-screen-on settings.
 * Mounted once, in App.vue.
 *
 * The lock is held exactly while `wanted` is true AND the page is visible: the browser
 * drops it by itself when the page is hidden and refuses a request made while hidden, so
 * every change of either one goes through `syncWakeLock`, which asks again or lets go.
 */
export function useKeepScreenOn() {
  const settings = useSettingsStore()
  const { keepScreenOnMode, keepScreenOnIdleMinutes } = storeToRefs(settings)
  const tabStore = useTabStore()
  const bookViewStore = useBookViewStore()
  const visibility = useDocumentVisibility()

  // Re-read on resize: F11 changes the window size, nothing else tells us.
  const { height } = useWindowSize()
  const isFullscreen = computed(() => (height.value, isWindowFullscreen()))

  const isReading = computed(() => {
    if (READING_ROUTES.has(tabStore.activeTabForPane(1).route)) return true
    return bookViewStore.splitViewEnabled && READING_ROUTES.has(tabStore.activeTabForPane(2).route)
  })

  // Idle limit. The last-activity time is a plain variable on purpose: it is written on
  // every scroll and wheel event, and nothing may re-run per event. Only the idle flag
  // is reactive, and it flips at most once per idle period.
  let lastActivityAt = Date.now()
  const isIdle = ref(false)
  function markActive() {
    lastActivityAt = Date.now()
    if (isIdle.value) isIdle.value = false
  }
  function checkIdle() {
    const minutes = keepScreenOnIdleMinutes.value
    isIdle.value = minutes > 0 && Date.now() - lastActivityAt > minutes * 60_000
  }
  for (const type of ACTIVITY_EVENTS)
    useEventListener(window, type, markActive, { capture: true, passive: true })

  // PDFs and HTML files live in iframes, and events inside an iframe never reach this
  // window. A frame we can reach (the PDF viewer) gets the same listeners when it loads -
  // they die with its document, so there is nothing to remove. A frame we cannot reach
  // reports its scrolling and keys by message (the host's injected frame script).
  useEventListener(document, 'load', (e: Event) => {
    if (!(e.target instanceof HTMLIFrameElement)) return
    let frameDocument: Document | null = null
    try { frameDocument = e.target.contentDocument } catch { /* cross-origin */ }
    if (!frameDocument) return
    for (const type of ACTIVITY_EVENTS)
      frameDocument.addEventListener(type, markActive, { capture: true, passive: true })
  }, { capture: true })
  useEventListener(window, 'message', (e: MessageEvent) => {
    const type = e.data?.type
    if (type === 'htmlViewScroll' || type === 'iframeKeydown') markActive()
  })

  const wanted = computed(() => {
    if (isIdle.value) return false
    switch (keepScreenOnMode.value) {
      case 'always': return true
      case 'reading': return isReading.value
      case 'fullscreen': return isFullscreen.value
      default: return false
    }
  })

  let heldLock: WakeLockSentinel | null = null
  let isRequesting = false
  const shouldHold = () => wanted.value && visibility.value === 'visible'

  async function syncWakeLock() {
    if (!('wakeLock' in navigator)) return
    if (!shouldHold()) {
      const lock = heldLock
      heldLock = null
      if (lock && !lock.released) await lock.release().catch(() => {})
      return
    }
    if (heldLock || isRequesting) return
    isRequesting = true
    try {
      const lock = await navigator.wakeLock.request('screen')
      // The browser lets go by itself when the page is hidden.
      lock.addEventListener('release', () => { if (heldLock === lock) heldLock = null })
      heldLock = lock
    } catch {
      // Refused (page not visible yet, battery saver). The next check asks again.
    } finally {
      isRequesting = false
    }
    // The answer may have changed while the request was in flight.
    if (!shouldHold()) void syncWakeLock()
  }

  watch([wanted, visibility], syncWakeLock, { immediate: true })
  watch(keepScreenOnIdleMinutes, checkIdle)
  useIntervalFn(() => {
    checkIdle()
    void syncWakeLock()
  }, CHECK_INTERVAL_MS)
}
