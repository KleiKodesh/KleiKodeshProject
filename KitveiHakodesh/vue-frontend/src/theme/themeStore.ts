import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { lsGet, lsSet } from '@/utils/persistence'

/** Disk name for the theme preference. Nothing else reads it. */
const KEYS = { SETTINGS_THEME: 'app.theme' } as const
import { applyTheme, getTheme, tintTheme, BACKGROUND_TINT_DEFAULT, toggleThemeMode, type ThemePreset } from './themes'
import { darken, lighten } from './themeColorUtils'
import { setTheme } from '@/webview-host/bridge'
import { uiLanguage } from '@/i18n'
export type { ThemePreset } from './themes'

interface ThemeState {
  themePreset: ThemePreset
  readingBackground: string
  backgroundTint?: number
}

export const useThemeStore = defineStore('theme', () => {
  const themePreset = ref<ThemePreset>('default-light')
  const readingBackground = ref('default')
  /** How strongly the theme's background color shows, % of the designed tint; see tintTheme. */
  const backgroundTint = ref(BACKGROUND_TINT_DEFAULT)

  // Synchronous — theme is in localStorage
  function init() {
    const saved = lsGet<ThemeState>(KEYS.SETTINGS_THEME)
    if (saved?.themePreset && getTheme(saved.themePreset)) themePreset.value = saved.themePreset
    if (saved?.readingBackground) readingBackground.value = saved.readingBackground
    if (typeof saved?.backgroundTint === 'number') backgroundTint.value = saved.backgroundTint

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
    applyTheme(themePreset.value, backgroundTint.value)
    if (readingBackground.value !== 'default') {
      const bgTheme = getTheme(readingBackground.value as ThemePreset)
      const bg = bgTheme && tintTheme(bgTheme, backgroundTint.value)
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
  // native chrome tab strip. The accent drives the active-tab indicator in the
  // native tab-list dropdown.
  //
  // The strip does NOT use ui.bgSecondary directly (the app's own title-bar
  // surface). Browser convention makes the tab strip a slightly MORE toned
  // surface, so the OS chrome reads as a recessed frame rather than blending
  // flat into the app. We push one step further from the content in the theme's
  // own direction — darker on light themes, lighter on dark. FluentChromeTabs
  // then derives the active tab as ~halfway back toward the content, which lands
  // near bgSecondary again: the active tab connects to the Vue title-bar row just
  // below the strip while the inactive strip sits behind it.
  function syncHostTheme() {
    const baseTheme = getTheme(themePreset.value)
    const theme = baseTheme && tintTheme(baseTheme, backgroundTint.value)
    const ui = theme?.ui
    const chromeStrip = ui
      ? theme!.isDark
        ? lighten(ui.bgSecondary, 6)
        : darken(ui.bgSecondary, 8)
      : undefined
    // uiLanguage rides along: a language switch reloads the page, so this startup send
    // carries it to the native chrome (direction + texts).
    setTheme(themePreset.value.includes('-dark'), chromeStrip, ui?.accentColor, ui?.borderColor, uiLanguage)
  }

  /** Momentary hover preview — paints `key` without saving; null restores the chosen theme. */
  function previewTheme(key: ThemePreset | null) {
    // A hovered theme previews at its designed tint — what picking it gives (ThemePicker.select).
    if (key) applyTheme(key, BACKGROUND_TINT_DEFAULT)
    else apply()
  }

  // Apply defaults immediately (before async init) so the UI doesn't flash
  apply()

  watch([themePreset, readingBackground, backgroundTint], () => {
    lsSet<ThemeState>(KEYS.SETTINGS_THEME, {
      themePreset: themePreset.value,
      readingBackground: readingBackground.value,
      backgroundTint: backgroundTint.value,
    })
    apply()
    syncHostTheme()
  })

  return { themePreset, readingBackground, backgroundTint, toggleDarkMode, previewTheme, init }
})
