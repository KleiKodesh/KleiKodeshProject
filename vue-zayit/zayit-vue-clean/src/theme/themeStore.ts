import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { applyTheme, getTheme, toggleThemeMode, type ThemePreset } from './themes'
export type { ThemePreset } from './themes'

const KEY = 'zayit-theme'

export const useThemeStore = defineStore('theme', () => {
  const themePreset = ref<ThemePreset>('fluent-light')
  const readingBackground = ref('default')

  function load() {
    try {
      const d = JSON.parse(localStorage.getItem(KEY) ?? '{}')
      if (d.themePreset && getTheme(d.themePreset)) themePreset.value = d.themePreset
      if (d.readingBackground) readingBackground.value = d.readingBackground
    } catch { /* noop */ }
  }

  function apply() {
    applyTheme(themePreset.value)
    if (readingBackground.value !== 'default') {
      const bg = getTheme(readingBackground.value as ThemePreset)
      if (bg) {
        const s = document.documentElement.style
        s.setProperty('--reading-bg-primary', bg.reading.bgPrimary)
        s.setProperty('--reading-bg-secondary', bg.reading.bgSecondary)
        s.setProperty('--reading-text-primary', bg.reading.textPrimary)
        s.setProperty('--reading-text-secondary', bg.reading.textSecondary)
        s.setProperty('--reading-border-color', bg.reading.borderColor)
      }
    }
  }

  function toggleDarkMode() { themePreset.value = toggleThemeMode(themePreset.value) }

  load()
  apply()
  watch([themePreset, readingBackground], () => {
    try { localStorage.setItem(KEY, JSON.stringify({ themePreset: themePreset.value, readingBackground: readingBackground.value })) } catch { /* noop */ }
    apply()
  })

  return { themePreset, readingBackground, toggleDarkMode }
})
