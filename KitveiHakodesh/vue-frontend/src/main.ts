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
import { useBooksDataStore } from './stores/booksDataStore'
import { useLocalFileStore } from './stores/localFileStore'
import { useHebrewBooksHistoryStore } from './stores/hebrewBooksHistoryStore'
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

app.mount('#app')

initPdfThemeObserver()

function warmBooksDataInBackground() {
  if (!dbReady.value) return
  // Delay briefly so the initial render and any active book-view line fetches
  // settle first, then kick off the catalog load in the background.
  window.setTimeout(() => {
    void useBooksDataStore().ensureLoaded()
  }, 500)
}
warmBooksDataInBackground()

// Pre-warm the Hebrew Books history cache so the first navigation to the
// Hebrew Books page renders history instantly from memory.
void useHebrewBooksHistoryStore().getHistory()

// Restore persisted local file tabs after mount so the UI paints immediately.
// PDF/HTML tabs render their loading placeholder right away; the virtual URL
// is filled in asynchronously once the C# bridge confirms the file is ready.
const localFileStore = useLocalFileStore()
const tabStore = useTabStore()
void Promise.all(
  tabStore.tabs
    .filter((t) => t.route === '/pdf-view' || t.route === '/html-view')
    .map((t) => localFileStore.restoreTab(t.id)),
)

// Signal C# that the Vue app has fully mounted and all event listeners are registered.
// C# uses this to dispatch any pending file path from an "Open With" launch — this
// replaces the unreliable fixed 1500ms delay that would drop the event on slow machines.
if (isHosted) {
  window.chrome?.webview?.postMessage({ id: '0', action: 'appReady' })
}
