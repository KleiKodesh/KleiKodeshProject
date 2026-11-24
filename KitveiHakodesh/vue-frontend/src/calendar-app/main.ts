// The standalone calendar: the app's own calendar page (with today's zmanim and daily
// learning beside it) as one offline file, opened by the installer's shortcut in an Edge
// app window. No service, no library: a click on a cycle hands a daily-learning link to
// the Kitvei Hakodesh app. Built by vite.calendar.config.ts.
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { configure } from 'kleikodesh-zmanim'
import { i18n, uiLanguage } from '@/i18n'
import '@/assets/styles/main.css'
import { useThemeStore } from '@/theme/themeStore'
import { DAILY_LEARNING_OPENER_KEY } from '@/features/home/useHomeDailyLearning'
import { dailyLearningLink } from '@/utils/dailyLearningLink'
import { zmanimAssetUrls } from 'virtual:zmanim-inline-assets'
import CalendarApp from './CalendarApp.vue'

// A page opened from disk cannot fetch files beside it: the zmanim data is inlined.
configure({
  loader: async (file) => {
    const url = zmanimAssetUrls[file]
    if (!url) throw new Error(`no zmanim asset ${file}`)
    return (await fetch(url)).arrayBuffer()
  },
})

document.documentElement.lang = uiLanguage
document.documentElement.dir = uiLanguage === 'en' ? 'ltr' : 'rtl'
const app = createApp(CalendarApp).use(createPinia()).use(i18n)
// Not the settings store: its startup asks the service (absent here) for app-only settings.
useThemeStore().init()
app.provide(DAILY_LEARNING_OPENER_KEY, (date, key) => {
  window.location.href = dailyLearningLink(key, date)
})
app.mount('#app')
