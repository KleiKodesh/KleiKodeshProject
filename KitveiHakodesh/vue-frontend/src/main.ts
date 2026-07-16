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
import { dbReady, isHosted } from './webview-host/seforimDb'
import { serviceCallVoid } from './webview-host/serviceClient'
import { initTabMirror } from './webview-host/tabMirror'
import { useBooksDataStore } from './stores/booksDataStore'
import { useLocalFileStore } from './stores/localFileStore'
import { useHostSearchStore } from './stores/hostSearchStore'
import { idbCheckAndExecReset } from './utils/persistence'

// Synchronous localStorage check — zero cost on normal boots.
// Only opens IDB if a reset was scheduled (rare safety net).
await idbCheckAndExecReset()

const pinia = createPinia()
const app = createApp(App).use(pinia)

// All synchronous — reads from localStorage
useWorkspaceStore().init()
useSettingsStore().init()
useBookViewStore().init()
useThemeStore().init()
useTabStore().init()

// Mirror the tab store to the native chrome tab strip (no-op in dev / VSTO).
initTabMirror()

app.mount('#app')

initPdfThemeObserver()

// Dev: tell the service to pay its one-time cold costs NOW (SQLite native lib, first
// connection to the 7GB DB, catalog cache, JIT of the read paths) — while the user is
// still looking at the home screen — instead of on their first book click. The service
// deliberately does nothing at its own boot; a loaded client is the signal that work
// is coming. Fire-and-forget; hosted mode has its own in-process warm path.
if (typeof window.__webviewAction !== 'function') serviceCallVoid('dbWarmup')

function warmBooksDataInBackground() {
  if (!dbReady.value) return
  // Delay briefly so the initial render and any active book-view line fetches
  // settle first, then kick off the catalog load in the background.
  window.setTimeout(() => {
    void useBooksDataStore().ensureLoaded()
  }, 500)
}
warmBooksDataInBackground()

// Restore persisted local file tabs after mount so the UI paints immediately.
// PDF/HTML tabs render their loading placeholder right away; the virtual URL
// is filled in asynchronously once the C# bridge confirms the file is ready.
const localFileStore = useLocalFileStore()
// Register the host-search listener before 'appReady' is posted below, so any search
// request queued C#-side (context menu clicked before Vue mounted) is delivered.
useHostSearchStore()
const tabStore = useTabStore()
void Promise.all(
  tabStore.tabs
    .filter((t) => t.route === '/pdf-view' || t.route === '/html-view' || t.route === '/txt-view')
    .map((t) => localFileStore.restoreTab(t.id)),
)

// Signal C# that the Vue app has fully mounted and all event listeners are registered.
// C# uses this to dispatch any pending file path from an "Open With" launch — this
// replaces the unreliable fixed 1500ms delay that would drop the event on slow machines.
if (isHosted) {
  window.chrome?.webview?.postMessage({ id: '0', action: 'appReady' })
}
