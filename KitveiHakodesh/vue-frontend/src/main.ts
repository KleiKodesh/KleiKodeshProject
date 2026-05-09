import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import './assets/styles/main.css'
import { useWorkspaceStore } from './stores/workspaceStore'
import { useTabStore } from './stores/tabStore'
import { useBookViewStore } from './stores/bookViewStore'
import { useSettingsStore } from './stores/settingsStore'
import { useThemeStore } from './theme/themeStore'
import { initPdfThemeObserver } from './theme/themes'
import { dbReady } from './webview-host/seforimDb'
import { useBooksDataStore } from './stores/booksDataStore'
import { usePdfStore } from './stores/pdfStore'
import { idbCheckAndExecReset } from './utils/persistence'

// Synchronous localStorage check — zero cost on normal boots.
// Only opens IDB if a reset was scheduled (rare).
await idbCheckAndExecReset()

const pinia = createPinia()
const app = createApp(App).use(pinia)

// All synchronous — reads from localStorage
useWorkspaceStore().init()
useSettingsStore().init()
useBookViewStore().init()
useThemeStore().init()
useTabStore().init()

// Restore any persisted PDF tabs — must run after tabStore.init()
const pdfStore = usePdfStore()
const tabStore = useTabStore()
await Promise.all([
  ...tabStore.tabs.filter((t) => t.route === '/pdf-view').map((t) => pdfStore.restoreTab(t.id)),
])

function warmBooksDataInBackground() {
  if (!dbReady.value) return

  const run = () => {
    void useBooksDataStore().ensureLoaded()
  }

  // Wait for the browser to be genuinely idle before loading the catalog.
  // A minimum wall-clock delay of 8s ensures the book view's line chunks have
  // all finished before the catalog queries compete for the DB connection.
  const MIN_DELAY = 8000
  const startTime = Date.now()

  if (typeof window.requestIdleCallback === 'function') {
    const tryIdle = () => {
      window.requestIdleCallback(
        (deadline) => {
          const elapsed = Date.now() - startTime
          if (elapsed >= MIN_DELAY && deadline.timeRemaining() > 50) {
            run()
          } else {
            // Not ready yet — reschedule
            window.setTimeout(tryIdle, Math.max(0, MIN_DELAY - elapsed))
          }
        },
        { timeout: MIN_DELAY + 5000 },
      )
    }
    window.setTimeout(tryIdle, MIN_DELAY)
  } else {
    window.setTimeout(run, MIN_DELAY)
  }
}

app.mount('#app')

initPdfThemeObserver()
warmBooksDataInBackground()
