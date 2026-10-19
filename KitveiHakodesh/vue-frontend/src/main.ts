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
import { dbReady, hasHostBridge, probeDbReady } from './webview-host/seforimDb'
import { initTabMirror } from './webview-host/tabMirror'
import { useBooksDataStore } from './stores/booksDataStore'
import { restoreLocalFileTabsLazily } from './stores/localFileTabLazyRestore'
import { useHostSearchStore } from './stores/hostSearchStore'
import { checkAndExecPendingReset } from './features/settings/appResetState'
import { checkAndExecPendingDbSwitchCleanup } from './webview-host/dbSwitchCleanup'
import { installBrowserPersistenceFlush, installHostPersistenceFlush } from './webview-host/persistenceFlush'
import { whenStartupContentDrawn } from './webview-host/serviceIdle'

// Synchronous localStorage check — zero cost on normal boots.
// Only opens IDB if a reset was scheduled (rare safety net).
await checkAndExecPendingReset()

// Same shape, for a library switch that died between the wipe and the reload. MUST
// stay above the store init below: it finishes the wipe in place, so the stores then
// read the cleaned state on this boot rather than needing another reload.
await checkAndExecPendingDbSwitchCleanup()

// Before any store or view exists: hide / reload / close flush every pending
// persistence write from here, position captures first (persistenceFlush.ts).
installBrowserPersistenceFlush()

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
// The service says whether the resolved DB exists; the catalog warm-up waits for that
// answer so a missing library does not fire queries that can only fail.
void probeDbReady().then(warmBooksDataInBackground)

// Register the host-search listener before 'appReady' is posted below, so any search
// request queued C#-side (context menu clicked before Vue mounted) is delivered.
useHostSearchStore()
// Restore persisted local file tabs after mount so the UI paints immediately: only the
// active tab per pane now, the rest when first shown (see localFileTabLazyRestore).
restoreLocalFileTabsLazily()

// Signal C# that the Vue app has fully mounted and all event listeners are registered.
// C# uses this to dispatch any pending file path from an "Open With" launch — this
// replaces the unreliable fixed 1500ms delay that would drop the event on slow machines.
if (hasHostBridge) {
  // The host closes its window without firing pagehide/beforeunload; this is how it
  // asks for every pending persistence write first (AppViewer.FlushPersistenceAsync).
  installHostPersistenceFlush()
  window.chrome?.webview?.postMessage({ id: '0', action: 'appReady' })
  // The desktop host keeps its last-session snapshot up until this: hiding it on
  // 'appReady' would show the restored view empty while its content is still loading.
  void whenStartupContentDrawn().then(() =>
    window.chrome?.webview?.postMessage({ id: '0', action: 'contentReady' }),
  )
}

// DEV-only test hook: lets the Playwright rigs open a local file and inspect
// tab/PDF-edit state without UI scraping. Stripped from production builds by
// the import.meta.env.DEV guard (dead-code eliminated).
if (import.meta.env.DEV) {
  void import('./devTestHook').then((m) => m.installDevTestHook())
}
