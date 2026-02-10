import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import './main.css'
import { initTheme } from './utils/theme'
import { initializeOfflineIcons } from './utils/iconify-offline'

// Import vue3-virtual-scroller components
import { DynamicScroller, DynamicScrollerItem } from 'vue3-virtual-scroller'
import 'vue3-virtual-scroller/dist/vue3-virtual-scroller.css'

// CRITICAL: Disable Ctrl+F immediately before anything else loads
// This prevents browser search and allows components to handle it
document.addEventListener('keydown', (event: KeyboardEvent) => {
    if ((event.ctrlKey || event.metaKey) && event.key === 'f') {
        event.preventDefault()
        // Don't use stopPropagation - let components handle it
    }
}, { capture: true })

// Initialize offline icons for WebView2 environment
initializeOfflineIcons()

// Initialize theme before anything else
initTheme()

const pinia = createPinia()
const app = createApp(App)

app.use(pinia)

// Register vue3-virtual-scroller components globally
app.component('DynamicScroller', DynamicScroller)
app.component('DynamicScrollerItem', DynamicScrollerItem)

// Initialize settings before mounting
import { useSettingsStore } from './stores/settingsStore'
import { useConnectionTypesStore } from './stores/connectionTypesStore'

useSettingsStore()

// Initialize connection types on app startup
const connectionTypesStore = useConnectionTypesStore()
connectionTypesStore.loadConnectionTypes()

app.mount('#app')
