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
import { useBooksDataStore } from './stores/booksDataStore'
import { usePdfStore } from './stores/pdfStore'
import { idbCheckAndExecReset } from './utils/persistence'

const _t0 = performance.now()
const _log = (label: string) =>
  console.log(`[Timing] ${label}: ${(performance.now() - _t0).toFixed(1)}ms`)

// Synchronous localStorage check — zero cost on normal boots.
// Only opens IDB if a reset was scheduled (rare).
await idbCheckAndExecReset()
_log('idbCheckAndExecReset')

const pinia = createPinia()
const app = createApp(App).use(pinia)

// All synchronous — reads from localStorage
useWorkspaceStore().init()
useSettingsStore().init()
useBookViewStore().init()
useThemeStore().init()
useTabStore().init()
_log('stores init')

// Restore any persisted PDF tabs — must run after tabStore.init()
const pdfStore = usePdfStore()
const tabStore = useTabStore()
await Promise.all(
  tabStore.tabs.filter((t) => t.route === '/pdf-view').map((t) => pdfStore.restoreTab(t.id)),
)
_log('pdf restore')

app.mount('#app')
_log('mount')

initPdfThemeObserver()
useBooksDataStore().ensureLoaded()
