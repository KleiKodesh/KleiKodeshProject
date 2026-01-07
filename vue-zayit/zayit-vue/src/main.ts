import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import './main.css'
import { initTheme } from './utils/theme'

// Initialize theme before anything else
initTheme()

const pinia = createPinia()
const app = createApp(App)

app.use(pinia)

// Initialize settings before mounting
import { useSettingsStore } from './stores/settingsStore'
useSettingsStore()

app.mount('#app')
