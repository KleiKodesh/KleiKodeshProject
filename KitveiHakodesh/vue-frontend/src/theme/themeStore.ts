import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { lsGet, lsSet, KEYS } from '@/utils/persistence'
import { applyTheme, getTheme, toggleThemeMode, type ThemePreset } from './themes'
import { setTheme } from '@/webview-host/bridge'
export type { ThemePreset } from './themes'

interface ThemeState {
  themePreset: ThemePreset
  readingBackground: string
}

export const useThemeStore = defineStore('theme', () => {
  const themePreset = ref<ThemePreset>('default-light')
  const readingBackground = ref('default')

  // Synchronous — theme is in localStorage
  function init() {
    const saved = lsGet<ThemeState>(KEYS.SETTINGS_THEME)
    if (saved?.themePreset && getTheme(saved.themePreset)) themePreset.value = saved.themePreset
    if (saved?.readingBackground) readingBackground.value = saved.readingBackground

    // If the C# host injected a persisted dark mode value, use it to override
    // the localStorage theme on startup. This ensures the Vue theme always
    // matches the title bar when multiple hosts share the same registry setting
    // (e.g. user set dark mode in VSTO, then opens the standalone demo app).
    const hostIsDark = window.__webviewIsDark
    if (typeof hostIsDark === 'boolean') {
      const currentIsDark = themePreset.value.includes('-dark')
      if (hostIsDark !== currentIsDark) {
        themePreset.value = toggleThemeMode(themePreset.value)
      }
    }

    apply()
    // Always send the theme (with the chrome-strip color) once on startup — the
    // watch below only fires on changes, and the native tab strip needs the color
    // even when the persisted preset already matches the host's dark flag.
    syncHostTheme()
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

  function toggleDarkMode() {
    themePreset.value = toggleThemeMode(themePreset.value)
  }

  // Notify the C# host so it can update the WinForms title bar (DarkNet) and the
  // native chrome tab strip. The strip derives its palette from the theme's
  // title-bar background (ui.bgSecondary — the same color the Vue title bar uses);
  // the accent drives the active-tab indicator in the native tab-list dropdown.
  function syncHostTheme() {
    const ui = getTheme(themePreset.value)?.ui
    setTheme(themePreset.value.includes('-dark'), ui?.bgSecondary, ui?.accentColor, ui?.borderColor)
  }

  // Apply defaults immediately (before async init) so the UI doesn't flash
  apply()

  watch([themePreset, readingBackground], () => {
    lsSet<ThemeState>(KEYS.SETTINGS_THEME, {
      themePreset: themePreset.value,
      readingBackground: readingBackground.value,
    })
    apply()
    syncHostTheme()
  })

  return { themePreset, readingBackground, toggleDarkMode, init }
})
