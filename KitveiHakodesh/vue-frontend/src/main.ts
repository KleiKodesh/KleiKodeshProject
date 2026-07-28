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
import { dbReady, hasHostBridge } from './webview-host/seforimDb'
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

// Warm the catalog as early as possible so it's already in the store when the user
// opens it. The catalog load (ensureLoaded → getAllCategories/getAllBooks) IS the
// service warm-up: it pays the one-time cold costs (SQLite native lib, first
// connection to the 7GB DB, catalog cache, JIT of the read paths) AND its result
// populates the Vue store — so a later catalog click is instant.
//
// Previously this fired a separate fire-and-forget `dbWarmup` at mount PLUS a
// 500ms-delayed ensureLoaded. The two raced the same cold connection: the real
// ensureLoaded queries fired (~500ms in) BEFORE dbWarmup had finished filling the
// service cache (~800ms), so they paid full cold cost anyway — measured 437/482ms
// instead of the warm ~5/50ms. Doing exactly ONE cold pass (ensureLoaded itself),
// kicked off right after first paint, removes that wasted double-work.
//
// requestAnimationFrame yields one frame so the initial render (and any restored
// book-view tab's own line fetch) isn't blocked; the catalog load then runs in the
// background alongside it — the service handles concurrent requests fine.
function warmBooksDataInBackground() {
  if (!dbReady.value) return
  requestAnimationFrame(() => {
    void useBooksDataStore().ensureLoaded()
  })
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
if (hasHostBridge) {
  window.chrome?.webview?.postMessage({ id: '0', action: 'appReady' })
}
