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
useSettingsStore()

app.mount('#app')
