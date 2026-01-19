import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import './main.css'
import { initTheme } from './utils/theme'
import { initializeOfflineIcons } from './utils/iconify-offline'

// Initialize offline icons for WebView2 environment
initializeOfflineIcons()

// Initialize theme before anything else
initTheme()

const pinia = createPinia()
const app = createApp(App)

app.use(pinia)

// Initialize settings before mounting
import { useSettingsStore } from './stores/settingsStore'
import { useConnectionTypesStore } from './stores/connectionTypesStore'

useSettingsStore()

// Initialize connection types on app startup
const connectionTypesStore = useConnectionTypesStore()
connectionTypesStore.loadConnectionTypes()

app.mount('#app')
