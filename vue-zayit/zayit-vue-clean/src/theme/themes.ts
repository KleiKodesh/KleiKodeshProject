import themesData from './themes.json'
import type { ThemePreset, Theme } from './themeTypes'
import { lighten, darken, hexToRgb, hexToRgbObj } from './themeColorUtils'
import { idbGet, idbSet, idbDelete, KEYS } from '@/utils/idbPersistence'

export type { ThemePreset, Theme, ThemeColors } from './themeTypes'
export { lighten, darken, hexToRgb, hexToRgbObj } from './themeColorUtils'

export const THEME_PRESETS: Record<ThemePreset, Theme> = themesData as Record<ThemePreset, Theme>

// ── Custom themes ────────────────────────────────────────────────────────────

let customThemes: Record<string, Theme> = {}

export async function loadCustomThemes() {
  customThemes = (await idbGet<Record<string, Theme>>(KEYS.SETTINGS_CUSTOM_THEMES)) ?? {}
}

function saveCustomThemes() {
  idbSet(KEYS.SETTINGS_CUSTOM_THEMES, customThemes)
}

export const getTheme = (preset: ThemePreset): Theme | undefined => THEME_PRESETS[preset] ?? customThemes[preset]
export const getAllThemes = (): Record<string, Theme> => ({ ...THEME_PRESETS, ...customThemes })
export const getCustomThemes = (): Record<string, Theme> => ({ ...customThemes })
export const isCustomTheme = (preset: ThemePreset): boolean => preset in customThemes

export function addCustomTheme(id: string, theme: Theme) { customThemes[id] = theme; saveCustomThemes() }
export function deleteCustomTheme(id: string) { delete customThemes[id]; saveCustomThemes() }

// ── Helpers ──────────────────────────────────────────────────────────────────

export function toggleThemeMode(current: ThemePreset): ThemePreset {
  const theme = THEME_PRESETS[current]
  if (!theme) return current
  const target = `${theme.family}-${theme.isDark ? 'light' : 'dark'}` as ThemePreset
  return THEME_PRESETS[target] ? target : current
}

export function getThemeFamilies() {
  const families = new Map<string, { name: string; lightPreset: ThemePreset; darkPreset: ThemePreset }>()
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

export const isDarkTheme = (): boolean => document.documentElement.classList.contains('dark')

// ── Apply theme to DOM ───────────────────────────────────────────────────────

export function applyTheme(preset: ThemePreset) {
  const theme = getTheme(preset)
  if (!theme) return

  const { ui, reading, isDark } = theme
  const s = document.documentElement.style

  document.documentElement.setAttribute('data-theme-preset', preset)
  document.documentElement.classList.toggle('dark', isDark)

  const uiVars: [string, string][] = [
    ['--bg-primary-custom', ui.bgPrimary],
    ['--bg-secondary-custom', ui.bgSecondary],
    ['--bg-tertiary-custom', ui.bgTertiary ?? ui.bgSecondary],
    ['--bg-toolbar-custom', ui.bgTertiary ?? ui.bgSecondary],
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

  syncPdfViewerTheme()
}

// ── PDF.js theme sync ────────────────────────────────────────────────────────

function calcPdfFilter(theme: Theme): string {
  if (theme.isDark) {
    const { r, g, b } = hexToRgbObj(theme.reading.accentColor)
    const [rv, gv, bv] = [r / 255, g / 255, b / 255]
    const max = Math.max(rv, gv, bv), min = Math.min(rv, gv, bv), delta = max - min
    let hue = 0
    if (delta) {
      if (max === rv) hue = 60 * (((gv - bv) / delta) % 6)
      else if (max === gv) hue = 60 * ((bv - rv) / delta + 2)
      else hue = 60 * ((rv - gv) / delta + 4)
    }
    if (hue < 0) hue += 360
    const sat = max === 0 ? 0 : delta / max
    let f = 'invert(0.9) hue-rotate(180deg)'
    if (sat > 0.3) f += ` sepia(${Math.min(0.9, sat * 1.2)}) hue-rotate(${Math.round(hue)}deg) saturate(${Math.min(1.6, 1.2 + sat * 0.8)})`
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
  const max = Math.max(rv, gv, bv), min = Math.min(rv, gv, bv), delta = max - min
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

export function syncPdfViewerTheme(): void {
  const isDark = isDarkTheme()
  const preset = document.documentElement.getAttribute('data-theme-preset')
  const theme = preset ? getTheme(preset as ThemePreset) : null

  document.querySelectorAll<HTMLIFrameElement>('iframe[src*="/pdfjs/web/viewer.html"]').forEach((iframe, i) => {
    try {
      const win = iframe.contentWindow as any
      if (!win?.PDFViewerApplicationOptions) return
      win.PDFViewerApplicationOptions.set('viewerCssTheme', isDark ? 2 : 1)

      const doc = win.document
      if (!doc?.documentElement) return

      doc.documentElement.style.setProperty('color-scheme', isDark ? 'dark' : 'light')
      doc.documentElement.classList.toggle('dark', isDark)

      if (preset) {
        const family = isCustomTheme(preset as ThemePreset) ? 'custom' : preset.split('-')[0]
        if (family) doc.documentElement.setAttribute('data-theme-family', family)
        const filter = theme?.pdfFilter ?? (theme ? calcPdfFilter(theme) : null)
        if (filter) doc.documentElement.style.setProperty('--pdf-page-filter', filter)
        else doc.documentElement.style.removeProperty('--pdf-page-filter')
      }

      const pdfFilters = document.documentElement.getAttribute('data-pdf-filters')
      if (pdfFilters) doc.documentElement.setAttribute('data-pdf-filters', pdfFilters)

      const rs = document.documentElement.style
      const ds = doc.documentElement.style
      for (const v of ['--bg-primary-custom', '--bg-secondary-custom', '--text-primary-custom', '--text-secondary-custom', '--border-color-custom', '--accent-color-custom', '--hover-bg-custom', '--active-bg-custom']) {
        const val = rs.getPropertyValue(v)
        if (val) ds.setProperty(v, val)
      }
    } catch (e) {
      console.warn(`[Theme] Could not access PDF iframe ${i + 1}:`, e)
    }
  })
}

export function initPdfThemeObserver(): void {
  if ((window as any).__pdfThemeObserverSetup) return
  ;(window as any).__pdfThemeObserverSetup = true

  new MutationObserver(mutations => {
    for (const { addedNodes } of mutations) {
      for (const node of addedNodes) {
        if (node.nodeType !== Node.ELEMENT_NODE) continue
        const el = node as Element
        const iframes = el.tagName === 'IFRAME'
          ? [el as HTMLIFrameElement]
          : Array.from(el.querySelectorAll?.('iframe[src*="/pdfjs/web/viewer.html"]') ?? []) as HTMLIFrameElement[]
        for (const iframe of iframes) {
          if (!iframe.src?.includes('/pdfjs/web/viewer.html')) continue
          iframe.addEventListener('load', () => setTimeout(syncPdfViewerTheme, 500))
          setTimeout(syncPdfViewerTheme, 200)
        }
      }
    }
  }).observe(document.body, { childList: true, subtree: true })
}
