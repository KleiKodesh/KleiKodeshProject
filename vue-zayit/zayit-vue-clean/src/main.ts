import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import router from './router'
import './assets/styles/main.css'

createApp(App).use(createPinia()).use(router).mount('#app')

import { useThemeStore } from './theme/themeStore'
import { initPdfThemeObserver } from './theme/themes'
useThemeStore()
initPdfThemeObserver()
