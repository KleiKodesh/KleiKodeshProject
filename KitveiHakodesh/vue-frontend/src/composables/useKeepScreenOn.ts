import { computed, ref, watch } from 'vue'
import { useEventListener, useWakeLock, useWindowSize, useIntervalFn } from '@vueuse/core'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { isWindowFullscreen } from '@/utils/isWindowFullscreen'

/** Tabs that count as "reading" for the 'reading' mode. */
const READING_ROUTES = new Set(['/book-view', '/fast-doc-view', '/pdf-view'])

/**
 * Holds the screen awake (Screen Wake Lock API) per the keep-screen-on settings.
 * Mounted once, in App.vue. The browser drops the lock by itself whenever the page is
 * hidden (minimized, other window on top in full screen); VueUse re-requests it when
 * the page shows again.
 */
export function useKeepScreenOn() {
  const settings = useSettingsStore()
  const { keepScreenOnMode, keepScreenOnIdleMinutes } = storeToRefs(settings)
  const tabStore = useTabStore()
  const bookViewStore = useBookViewStore()
  const { isSupported, isActive, request, release } = useWakeLock()

  // Re-read on resize: F11 changes the window size, nothing else tells us.
  const { height } = useWindowSize()
  const isFullscreen = computed(() => (height.value, isWindowFullscreen()))

  const isReading = computed(() => {
    if (READING_ROUTES.has(tabStore.activeTabForPane(1).route)) return true
    return bookViewStore.splitViewEnabled && READING_ROUTES.has(tabStore.activeTabForPane(2).route)
  })

  // Idle limit: any scroll, key, click or wheel counts as reading activity.
  const lastActivity = ref(Date.now())
  const now = ref(Date.now())
  const markActive = () => { lastActivity.value = now.value = Date.now() }
  for (const type of ['keydown', 'pointerdown', 'wheel', 'scroll'] as const)
    useEventListener(window, type, markActive, { capture: true, passive: true })
  useIntervalFn(() => { now.value = Date.now() }, 30_000)

  const isIdle = computed(() => {
    const minutes = keepScreenOnIdleMinutes.value
    return minutes > 0 && now.value - lastActivity.value > minutes * 60_000
  })

  const wanted = computed(() => {
    switch (keepScreenOnMode.value) {
      case 'always': return !isIdle.value
      case 'reading': return isReading.value && !isIdle.value
      case 'fullscreen': return isFullscreen.value && !isIdle.value
      default: return false
    }
  })

  watch(wanted, (on) => {
    if (!isSupported.value) return
    if (on && !isActive.value) request('screen').catch(() => {})
    else if (!on && isActive.value) release().catch(() => {})
  }, { immediate: true })

}
