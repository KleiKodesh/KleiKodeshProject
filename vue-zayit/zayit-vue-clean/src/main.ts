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

const pinia = createPinia()
const app = createApp(App).use(pinia)

// workspaceStore must init first — tabStore depends on activeId
await useWorkspaceStore().init()

// Init all remaining stores from IDB before mounting
await Promise.all([
  useTabStore().init(),
  useBookViewStore().init(),
  useSettingsStore().init(),
  useThemeStore().init(),
  loadCustomThemes(),
])

app.mount('#app')

initPdfThemeObserver()
useBooksDataStore().ensureLoaded()
