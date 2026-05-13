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

  // Delay briefly so the initial render and any active book-view line fetches
  // settle first, then kick off the catalog load in the background.
  // Plain setTimeout — no requestIdleCallback — so it fires predictably after
  // the delay rather than waiting indefinitely for an idle window that may
  // never come while the book view is streaming line chunks.
  window.setTimeout(() => {
    void useBooksDataStore().ensureLoaded()
  }, 500)
}

app.mount('#app')

initPdfThemeObserver()
warmBooksDataInBackground()
