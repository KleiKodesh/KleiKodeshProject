import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import './main.css'
import { initTheme } from '@/utils/themes'
import { initializeOfflineIcons } from '@/utils/iconify-offline'

// CRITICAL: Prevent browser default for Ctrl+F only
// Ctrl+A is handled by individual components to allow proper focus checking
console.log('[main.ts] Setting up global keyboard preventDefault')

window.addEventListener('keydown', (event: KeyboardEvent) => {
    if ((event.ctrlKey || event.metaKey)) {
        // Use event.code instead of event.key to work with any keyboard layout
        if (event.code === 'KeyF') {
            event.preventDefault()
        }
    }
}, { capture: true, passive: false })

console.log('[main.ts] Global keyboard handler installed')

// Initialize offline icons for WebView2 environment
initializeOfflineIcons()

// Initialize theme before anything else
initTheme()

const pinia = createPinia()
const app = createApp(App)

app.use(pinia)

// Initialize settings before mounting
import { useSettingsStore } from '@/data/stores/settingsStore'
import { useConnectionTypesStore } from '@/data/stores/connectionTypesStore'

useSettingsStore()

// Initialize connection types on app startup
const connectionTypesStore = useConnectionTypesStore()
connectionTypesStore.loadConnectionTypes()

app.mount('#app')
