/**
 * Local file search composable.
 *
 * Flow:
 * 1. On mount, check if the user has consented to installing the DocumentLocator service.
 *    - Not yet consented → installConsentPending = true. No bridge call is made.
 *    - Already consented → proceed to step 2.
 *
 * 2. Call fileSystemSearchPageLoad(). C# always replies { isReady: false } immediately
 *    and does all blocking work (StopIfStale, install, index wait) on a background thread.
 *    C# pushes fileSystemIndexingStatus events while the index builds:
 *      { event: 'fileSystemIndexingStatus', isIndexing: true,  message: 'Crawling C:…' }
 *      { event: 'fileSystemIndexingStatus', isIndexing: false }  ← index ready
 *
 * 3. The indexingMessage ref tracks the latest progress string from C# so the UI can
 *    show it inside the spinner. Cleared when isIndexing becomes false.
 *
 * 4. Search only fires when isIndexing = false and installConsentPending = false.
 *    If the user typed before the index was ready the isIndexing watcher retries.
 *
 * 5. Consent is stored in localStorage via KEYS.SETTINGS_DOCUMENT_LOCATOR_INSTALL_CONSENTED.
 *    "No" clears the key so the prompt reappears on the next page visit.
 *
 * 6. The webview event listener is registered once and never re-registered, guarded
 *    by _eventListenerRegistered so multiple startPageLoad calls are safe.
 */

import { ref, watch, onMounted, onUnmounted } from 'vue'
import { refDebounced } from '@vueuse/core'
import { fileSystemSearch, fileSystemSearchPageLoad } from '@/webview-host/bridge'
import { isHosted, onWebviewEvent } from '@/webview-host/seforimDb'
import { lsGet, lsSet, lsDelete, KEYS } from '@/utils/persistence'

export interface LocalFileSearchResult {
  fileName: string
  path: string
  fullPath: string
}

const DEBOUNCE_MS = 200
const MAX_RESULTS = 5000
const LOADING_ANIMATION_DELAY_MS = 200

export function useLocalFileSearch(searchQuery: ReturnType<typeof ref<string>>) {
  const results = ref<LocalFileSearchResult[]>([])
  const searching = ref(false)
  const showLoadingAnimation = ref(false)
  const isIndexing = ref(true)
  const indexingMessage = ref<string | null>(null) // progress text from C# during index build
  const totalCount = ref(0)
  const errorMessage = ref<string | null>(null)
  const installConsentPending = ref(false)

  let loadingAnimationTimer: ReturnType<typeof setTimeout> | null = null
  let _eventListenerRegistered = false

  function startLoadingAnimationTimer() {
    cancelLoadingAnimationTimer()
    loadingAnimationTimer = setTimeout(() => {
      showLoadingAnimation.value = true
    }, LOADING_ANIMATION_DELAY_MS)
  }

  function cancelLoadingAnimationTimer() {
    if (loadingAnimationTimer !== null) {
      clearTimeout(loadingAnimationTimer)
      loadingAnimationTimer = null
    }
    showLoadingAnimation.value = false
  }

  onUnmounted(() => cancelLoadingAnimationTimer())

  function registerEventListener() {
    if (_eventListenerRegistered) return
    _eventListenerRegistered = true

    onWebviewEvent((event: any) => {
      if (event.event !== 'fileSystemIndexingStatus') return

      if (event.isIndexing === false) {
        // Index is ready — clear the spinner and any progress message.
        indexingMessage.value = null
        isIndexing.value = false
      } else {
        // Still building — update the progress message if provided.
        if (typeof event.message === 'string') {
          indexingMessage.value = event.message
        }
        isIndexing.value = true
      }
    })
  }

  function startPageLoad() {
    registerEventListener()

    fileSystemSearchPageLoad()
      .then(() => {
        // C# always replies { isReady: false } and does the real work on a background thread.
        // The isIndexing state is driven entirely by fileSystemIndexingStatus push events.
        // Nothing to do here — keep isIndexing=true and wait for the push.
      })
      .catch(() => {
        // Bridge unavailable (dev mode without host) — unblock search.
        isIndexing.value = false
      })
  }

  /** User clicked "כן, התקן" — save consent and kick off the page-load handshake. */
  function onInstallConsentGranted() {
    lsSet(KEYS.SETTINGS_DOCUMENT_LOCATOR_INSTALL_CONSENTED, true)
    installConsentPending.value = false
    isIndexing.value = true
    startPageLoad()
  }

  /** User clicked "לא" — clear any stored consent so the prompt reappears next visit. */
  function onInstallConsentDeclined() {
    installConsentPending.value = false
    lsDelete(KEYS.SETTINGS_DOCUMENT_LOCATOR_INSTALL_CONSENTED)
    errorMessage.value = 'שירות האינדקס אינו מותקן. פתח את הדף שוב כדי להתקינו.'
    isIndexing.value = false
  }

  onMounted(() => {
    if (!isHosted) {
      isIndexing.value = false
      return
    }

    const alreadyConsented = lsGet<boolean>(KEYS.SETTINGS_DOCUMENT_LOCATOR_INSTALL_CONSENTED)
    if (alreadyConsented) {
      startPageLoad()
    } else {
      installConsentPending.value = true
      isIndexing.value = false
    }
  })

  const debouncedQuery = refDebounced(searchQuery, DEBOUNCE_MS)
  let generation = 0

  async function runSearch(rawQuery: string) {
    const thisGeneration = ++generation
    errorMessage.value = null

    const trimmed = (rawQuery ?? '').trim()
    if (!trimmed) {
      results.value = []
      totalCount.value = 0
      searching.value = false
      return
    }

    if (isIndexing.value || installConsentPending.value) {
      searching.value = false
      return
    }

    searching.value = true
    startLoadingAnimationTimer()

    try {
      const response = await fileSystemSearch(trimmed, MAX_RESULTS)
      if (thisGeneration !== generation) return

      if (response.notInstalled) {
        // Service was uninstalled while the app was running — clear consent and
        // re-show the install prompt so the user can reinstall.
        lsDelete(KEYS.SETTINGS_DOCUMENT_LOCATOR_INSTALL_CONSENTED)
        isIndexing.value = false
        installConsentPending.value = true
        results.value = []
        totalCount.value = 0
        return
      }

      if (response.error) {
        errorMessage.value = response.error
        results.value = []
        totalCount.value = 0
        return
      }

      totalCount.value = response.total ?? 0
      results.value = (response.results ?? []).map((item) => ({
        fileName: item.fileName,
        path: item.path,
        fullPath: item.path ? `${item.path}\\${item.fileName}` : item.fileName,
      }))
    } catch (error) {
      if (thisGeneration !== generation) return
      errorMessage.value = error instanceof Error ? error.message : 'שגיאה בחיפוש'
      results.value = []
      totalCount.value = 0
    } finally {
      if (thisGeneration === generation) {
        searching.value = false
        cancelLoadingAnimationTimer()
      }
    }
  }

  watch(debouncedQuery, (rawQuery) => runSearch(rawQuery ?? ''), { immediate: true })

  watch(isIndexing, (nowIndexing) => {
    if (!nowIndexing && (debouncedQuery.value ?? '').trim()) {
      runSearch(debouncedQuery.value ?? '')
    }
  })

  return {
    results,
    searching,
    showLoadingAnimation,
    isIndexing,
    indexingMessage,
    totalCount,
    errorMessage,
    installConsentPending,
    onInstallConsentGranted,
    onInstallConsentDeclined,
  }
}
