import themesData from './themes.json'
import type { ThemePreset, Theme, ThemeColors } from './themeTypes'
import { lighten, darken, hexToRgb, hexToRgbObj, mixHex } from './themeColorUtils'

export type { ThemePreset, Theme, ThemeColors } from './themeTypes'

export const THEME_PRESETS: Record<ThemePreset, Theme> = themesData as Record<ThemePreset, Theme>

export const getTheme = (preset: ThemePreset): Theme | undefined => THEME_PRESETS[preset]
export const getAllThemes = (): Record<ThemePreset, Theme> => ({ ...THEME_PRESETS })

// ── Helpers ──────────────────────────────────────────────────────────────────

export function toggleThemeMode(current: ThemePreset): ThemePreset {
  const theme = THEME_PRESETS[current]
  if (!theme) return current
  const target = `${theme.family}-${theme.isDark ? 'light' : 'dark'}` as ThemePreset
  return THEME_PRESETS[target] ? target : current
}

export function getThemeFamilies() {
  const families = new Map<
    string,
    { name: string; lightPreset: ThemePreset; darkPreset: ThemePreset }
  >()
  for (const theme of Object.values(THEME_PRESETS)) {
    if (!families.has(theme.family)) {
      families.set(theme.family, {
        name: theme.name,
        lightPreset: `${theme.family}-light` as ThemePreset,
        darkPreset: `${theme.family}-dark` as ThemePreset,
      })
    }
  }
  return Array.from(families.entries()).map(([family, data]) => ({ family, ...data }))
}

/** Background tint strength, in percent of the theme's own tint: 0 = neutral gray/white,
 *  100 = the theme as designed, 200 = twice as colored. */
export const BACKGROUND_TINT_MIN = 0
export const BACKGROUND_TINT_MAX = 200
export const BACKGROUND_TINT_DEFAULT = 100

/** Scales how colored (beige, green, rose...) a color is, keeping its brightness:
 *  the color is pulled toward / pushed away from the gray of the same brightness. */
function scaleTint(hex: string, strength: number): string {
  const { r, g, b } = hexToRgbObj(hex)
  const gray = 0.299 * r + 0.587 * g + 0.114 * b
  const ch = (x: number) => Math.min(255, Math.max(0, Math.round(gray + (x - gray) * strength)))
  return '#' + [ch(r), ch(g), ch(b)].map((x) => x.toString(16).padStart(2, '0')).join('')
}

/** False for themes whose backgrounds are plain white/gray (default, Fluent...):
 *  there is no tint for the tint slider to scale, so it is not offered. */
export function hasBackgroundTint(theme: Theme): boolean {
  return [theme.ui, theme.reading].some((c) =>
    [c.bgPrimary, c.bgSecondary].some((hex) => {
      const { r, g, b } = hexToRgbObj(hex)
      return Math.max(r, g, b) - Math.min(r, g, b) > 4
    }),
  )
}

/**
 * The theme with its background surfaces (and the borders on them) more or less
 * tinted — how emphasized the theme's background color is. Text and accent keep
 * their colors; brightness stays the same, so contrast is unchanged.
 */
export function tintTheme(theme: Theme, tint: number): Theme {
  if (tint === BACKGROUND_TINT_DEFAULT) return theme
  const strength = tint / 100
  const tintColors = (c: ThemeColors): ThemeColors => ({
    ...c,
    bgPrimary: scaleTint(c.bgPrimary, strength),
    bgSecondary: scaleTint(c.bgSecondary, strength),
    ...(c.bgTertiary ? { bgTertiary: scaleTint(c.bgTertiary, strength) } : {}),
    borderColor: scaleTint(c.borderColor, strength),
  })
  return { ...theme, ui: tintColors(theme.ui), reading: tintColors(theme.reading) }
}

export const isDarkTheme = (): boolean => document.documentElement.classList.contains('dark')

// ── Apply theme to DOM ───────────────────────────────────────────────────────

export function applyTheme(preset: ThemePreset, backgroundTint = BACKGROUND_TINT_DEFAULT) {
  const base = getTheme(preset)
  if (!base) return
  const theme = tintTheme(base, backgroundTint)

  const { ui, reading, isDark } = theme
  const s = document.documentElement.style

  // Hover fades must not replay as a lag when every colour changes at once:
  // suppress transitions for the frame the new variables land in.
  const root = document.documentElement
  root.classList.add('theme-switching')
  requestAnimationFrame(() => requestAnimationFrame(() => root.classList.remove('theme-switching')))

  document.documentElement.setAttribute('data-theme-preset', preset)
  document.documentElement.classList.toggle('dark', isDark)

  const uiVars: [string, string][] = [
    ['--bg-primary-custom', ui.bgPrimary],
    ['--bg-secondary-custom', ui.bgSecondary],
    // Fluent three-layer ramp: the toolbar (commanding layer) sits halfway
    // between the title bar (bgSecondary) and the content (bgPrimary) in every
    // theme — one step toward the content, never darker than the title bar.
    ['--bg-toolbar-custom', mixHex(ui.bgSecondary, ui.bgPrimary, 0.5)],
    ['--text-primary-custom', ui.textPrimary],
    ['--text-secondary-custom', ui.textSecondary],
    ['--border-color-custom', ui.borderColor],
    ['--accent-color-custom', ui.accentColor],
    ['--hover-bg-custom', ui.hoverBg],
    ['--active-bg-custom', ui.activeBg],
    ['--bg-primary-rgb-custom', hexToRgb(ui.bgPrimary)],
    ['--bg-secondary-rgb-custom', hexToRgb(ui.bgSecondary)],
    ['--ui-reading-bg', isDark ? lighten(ui.bgPrimary, 3) : darken(ui.bgPrimary, 2)],
  ]

  const readingVars: [string, string][] = [
    ['--reading-bg-primary', reading.bgPrimary],
    ['--reading-bg-secondary', reading.bgSecondary],
    ['--reading-text-primary', reading.textPrimary],
    ['--reading-text-secondary', reading.textSecondary],
    ['--reading-border-color', reading.borderColor],
    ['--reading-accent-color', reading.accentColor],
    ['--reading-hover-bg', reading.hoverBg],
    ['--reading-active-bg', reading.activeBg],
    ['--reading-bg-primary-rgb', hexToRgb(reading.bgPrimary)],
    ['--reading-bg-secondary-rgb', hexToRgb(reading.bgSecondary)],
  ]

  for (const [k, v] of [...uiVars, ...readingVars]) s.setProperty(k, v)

  const { r, g, b } = hexToRgbObj(ui.accentColor)
  s.setProperty('--accent-bg', `rgba(${r}, ${g}, ${b}, 0.1)`)
  s.setProperty('--accent-bg-light', `rgba(${r}, ${g}, ${b}, 0.05)`)
}
