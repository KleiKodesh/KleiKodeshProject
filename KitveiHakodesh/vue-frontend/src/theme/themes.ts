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

  // The PDF page colour filter (the PDF viewer's page canvases and thumbnails): the theme's own,
  // else one made from its colours. Applied only while the "PDF page filters" setting is on
  // (data-pdf-filters on <html>, see pdf-reader/pdfReaderTheme.css). Root style and attributes
  // are mirrored into every app window, so all windows follow live.
  s.setProperty('--pdf-page-filter', base.pdfFilter ?? calcPdfFilter(theme))
}

// -- PDF page filter ------------------------------------------------------------

/** The CSS filter that fits a PDF page to the theme: dark themes invert it (tinted by the accent
 *  colour), warm light themes add sepia, strongly coloured ones a hue tint; else "none". */
export function calcPdfFilter(theme: Theme): string {
  if (theme.isDark) {
    const { r, g, b } = hexToRgbObj(theme.reading.accentColor)
    const [rv, gv, bv] = [r / 255, g / 255, b / 255]
    const max = Math.max(rv, gv, bv),
      min = Math.min(rv, gv, bv),
      delta = max - min
    let hue = 0
    if (delta) {
      if (max === rv) hue = 60 * (((gv - bv) / delta) % 6)
      else if (max === gv) hue = 60 * ((bv - rv) / delta + 2)
      else hue = 60 * ((rv - gv) / delta + 4)
    }
    if (hue < 0) hue += 360
    const sat = max === 0 ? 0 : delta / max
    let f = 'invert(0.9) hue-rotate(180deg)'
    if (sat > 0.3)
      f += ` sepia(${Math.min(0.9, sat * 1.2)}) hue-rotate(${Math.round(hue)}deg) saturate(${Math.min(1.6, 1.2 + sat * 0.8)})`
    return f + ' brightness(0.8) contrast(0.9)'
  }

  const bg = hexToRgbObj(theme.reading.bgPrimary)
  const warmth = bg.r > bg.b && bg.g > bg.b ? (bg.r + bg.g - 2 * bg.b) / 255 : 0
  if (warmth > 0.2) {
    const s = Math.min(1, warmth * 2)
    return `sepia(${s}) brightness(${0.88 + (1 - s) * 0.04})`
  }
  const { r, g, b } = hexToRgbObj(theme.reading.accentColor)
  const [rv, gv, bv] = [r / 255, g / 255, b / 255]
  const max = Math.max(rv, gv, bv),
    min = Math.min(rv, gv, bv),
    delta = max - min
  const sat = max === 0 ? 0 : delta / max
  if (sat > 0.4) {
    let hue = 0
    if (delta) {
      if (max === rv) hue = 60 * (((gv - bv) / delta) % 6)
      else if (max === gv) hue = 60 * ((bv - rv) / delta + 2)
      else hue = 60 * ((rv - gv) / delta + 4)
    }
    if (hue < 0) hue += 360
    return `sepia(${Math.min(0.9, sat * 1.5)}) hue-rotate(${Math.round(hue)}deg) saturate(${Math.min(1.6, 1.2 + sat * 0.6)})`
  }
  return 'none'
}
