import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import './assets/styles/main.css'
import { useWorkspaceStore } from './stores/workspaceStore'
import { useTabStore } from './stores/tabStore'
import { useBookViewStore } from './stores/bookViewStore'
import { useSettingsStore } from './stores/settingsStore'
import { useThemeStore } from './theme/themeStore'
import { loadCustomThemes, initPdfThemeObserver } from './theme/themes'
import { useBooksDataStore } from './stores/booksDataStore'
import { usePdfStore } from './stores/pdfStore'
import { idbCheckAndExecReset } from './utils/idbPersistence'

const t0 = performance.now()
const mark = (label: string) => console.log(`[boot] ${label}: +${(performance.now() - t0).toFixed(1)}ms`)

// Synchronous localStorage check — zero cost on normal boots.
// Only opens IDB if a reset was scheduled (rare).
await idbCheckAndExecReset()
mark('idbCheckAndExecReset')

const pinia = createPinia()
const app = createApp(App).use(pinia)
mark('createApp')

// workspaceStore must init first — tabStore depends on activeId
await useWorkspaceStore().init()
mark('workspaceStore.init')

// Init all remaining stores from IDB before mounting
await Promise.all([
  useTabStore().init(),
  useBookViewStore().init(),
  useSettingsStore().init(),
  useThemeStore().init(),
  loadCustomThemes(),
])
mark('all stores init')

// Restore any persisted PDF tabs — must run after tabStore.init()
const pdfStore = usePdfStore()
const tabStore = useTabStore()
await Promise.all(
  tabStore.tabs.filter((t) => t.route === '/pdf-view').map((t) => pdfStore.restoreTab(t.id)),
)
mark('pdf restore')

app.mount('#app')
mark('mount')

initPdfThemeObserver()
useBooksDataStore().ensureLoaded()
