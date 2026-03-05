/**
 * Theme Configuration and Utilities
 * Reading-optimized themes + PDF.js viewer theme syncing
 */

import themesData from '@/data/themes.json'
import type { ThemePreset, Theme } from '@/data/themeTypes'
import { lighten, darken, hexToRgb, hexToRgbObj } from './themeColorUtils'

// Re-export types for convenience
export type { ThemePreset, Theme, ThemeColors } from '@/data/themeTypes'

// Re-export color utilities
export { lighten, darken, hexToRgb, hexToRgbObj } from './themeColorUtils'

// Load theme presets from JSON
export const THEME_PRESETS: Record<ThemePreset, Theme> = themesData as Record<ThemePreset, Theme>

// Calculate PDF filter based on theme colors
function calculatePdfFilter(theme: Theme): string {
    if (theme.isDark) {
        // Dark themes: always use inversion as base
        const bg = hexToRgbObj(theme.reading.bgPrimary)
        const accent = hexToRgbObj(theme.reading.accentColor)

        // Calculate hue from accent color
        const r = accent.r / 255
        const g = accent.g / 255
        const b = accent.b / 255
        const max = Math.max(r, g, b)
        const min = Math.min(r, g, b)
        const delta = max - min

        let hue = 0
        if (delta !== 0) {
            if (max === r) {
                hue = 60 * (((g - b) / delta) % 6)
            } else if (max === g) {
                hue = 60 * (((b - r) / delta) + 2)
            } else {
                hue = 60 * (((r - g) / delta) + 4)
            }
        }
        if (hue < 0) hue += 360

        // Calculate saturation
        const saturation = max === 0 ? 0 : delta / max

        // Build filter based on color characteristics
        let filter = 'invert(0.9) hue-rotate(180deg)'

        // Add color tint if accent is saturated
        if (saturation > 0.3) {
            const sepiaAmount = Math.min(0.9, saturation * 1.2)
            const hueShift = Math.round(hue)
            const satAmount = Math.min(1.6, 1.2 + saturation * 0.8)
            filter += ` sepia(${sepiaAmount}) hue-rotate(${hueShift}deg) saturate(${satAmount})`
        }

        filter += ' brightness(0.8) contrast(0.9)'
        return filter
    } else {
        // Light themes: add color tint based on background warmth
        const bg = hexToRgbObj(theme.reading.bgPrimary)

        // Check if background is warm (yellowish/sepia)
        const isWarm = bg.r > bg.b && bg.g > bg.b
        const warmth = isWarm ? (bg.r + bg.g - 2 * bg.b) / 255 : 0

        if (warmth > 0.2) {
            // Warm background: apply sepia
            const sepiaAmount = Math.min(1, warmth * 2)
            const brightness = 0.88 + (1 - sepiaAmount) * 0.04
            return `sepia(${sepiaAmount}) brightness(${brightness})`
        }

        // Check for color tint in background
        const accent = hexToRgbObj(theme.reading.accentColor)
        const r = accent.r / 255
        const g = accent.g / 255
        const b = accent.b / 255
        const max = Math.max(r, g, b)
        const min = Math.min(r, g, b)
        const delta = max - min
        const saturation = max === 0 ? 0 : delta / max

        if (saturation > 0.4) {
            // Saturated accent: apply color tint
            let hue = 0
            if (delta !== 0) {
                if (max === r) {
                    hue = 60 * (((g - b) / delta) % 6)
                } else if (max === g) {
                    hue = 60 * (((b - r) / delta) + 2)
                } else {
                    hue = 60 * (((r - g) / delta) + 4)
                }
            }
            if (hue < 0) hue += 360

            const sepiaAmount = Math.min(0.9, saturation * 1.5)
            const hueShift = Math.round(hue)
            const satAmount = Math.min(1.6, 1.2 + saturation * 0.6)
            return `sepia(${sepiaAmount}) hue-rotate(${hueShift}deg) saturate(${satAmount})`
        }

        // Neutral theme: no filter
        return 'none'
    }
}

// Apply theme to document
export function applyTheme(preset: ThemePreset) {
    const theme = getTheme(preset)
    if (!theme) return

    const uiColors = theme.ui
    const readingColors = theme.reading

    // Set theme preset as data attribute for PDF page filters
    document.documentElement.setAttribute('data-theme-preset', preset)

    // Set UI CSS custom properties (for general app UI)
    document.documentElement.style.setProperty('--bg-primary-custom', uiColors.bgPrimary)
    document.documentElement.style.setProperty('--bg-secondary-custom', uiColors.bgSecondary)
    document.documentElement.style.setProperty('--bg-tertiary-custom', uiColors.bgTertiary || uiColors.bgSecondary) // Fallback to secondary
    document.documentElement.style.setProperty('--text-primary-custom', uiColors.textPrimary)
    document.documentElement.style.setProperty('--text-secondary-custom', uiColors.textSecondary)
    document.documentElement.style.setProperty('--border-color-custom', uiColors.borderColor)
    document.documentElement.style.setProperty('--accent-color-custom', uiColors.accentColor)
    document.documentElement.style.setProperty('--hover-bg-custom', uiColors.hoverBg)
    document.documentElement.style.setProperty('--active-bg-custom', uiColors.activeBg)

    // Set Reading area CSS custom properties (for LineView and CommentaryView)
    document.documentElement.style.setProperty('--reading-bg-primary', readingColors.bgPrimary)
    document.documentElement.style.setProperty('--reading-bg-secondary', readingColors.bgSecondary)
    document.documentElement.style.setProperty('--reading-text-primary', readingColors.textPrimary)
    document.documentElement.style.setProperty('--reading-text-secondary', readingColors.textSecondary)
    document.documentElement.style.setProperty('--reading-border-color', readingColors.borderColor)
    document.documentElement.style.setProperty('--reading-accent-color', readingColors.accentColor)
    document.documentElement.style.setProperty('--reading-hover-bg', readingColors.hoverBg)
    document.documentElement.style.setProperty('--reading-active-bg', readingColors.activeBg)

    // Set UI Reading background (for settings page, etc.) - calculated from UI colors, not content reading colors
    // This provides a softer background for UI pages without being affected by custom reading backgrounds
    const uiReadingBg = theme.isDark
        ? lighten(uiColors.bgPrimary, 3)
        : darken(uiColors.bgPrimary, 2)
    document.documentElement.style.setProperty('--ui-reading-bg', uiReadingBg)

    // Calculate RGB values for transparency
    const bgPrimaryRgb = hexToRgb(uiColors.bgPrimary)
    const bgSecondaryRgb = hexToRgb(uiColors.bgSecondary)
    document.documentElement.style.setProperty('--bg-primary-rgb-custom', bgPrimaryRgb)
    document.documentElement.style.setProperty('--bg-secondary-rgb-custom', bgSecondaryRgb)

    const readingBgPrimaryRgb = hexToRgb(readingColors.bgPrimary)
    const readingBgSecondaryRgb = hexToRgb(readingColors.bgSecondary)
    document.documentElement.style.setProperty('--reading-bg-primary-rgb', readingBgPrimaryRgb)
    document.documentElement.style.setProperty('--reading-bg-secondary-rgb', readingBgSecondaryRgb)

    // Calculate accent background colors
    const accentRgbObj = hexToRgbObj(uiColors.accentColor)
    document.documentElement.style.setProperty('--accent-bg', `rgba(${accentRgbObj.r}, ${accentRgbObj.g}, ${accentRgbObj.b}, 0.1)`)
    document.documentElement.style.setProperty('--accent-bg-light', `rgba(${accentRgbObj.r}, ${accentRgbObj.g}, ${accentRgbObj.b}, 0.05)`)

    // Apply dark class if theme is dark
    if (theme.isDark) {
        document.documentElement.classList.add('dark')
    } else {
        document.documentElement.classList.remove('dark')
    }
}

// Toggle between light and dark variant of current theme
export function toggleThemeMode(currentPreset: ThemePreset): ThemePreset {
    const currentTheme = THEME_PRESETS[currentPreset]
    if (!currentTheme) return currentPreset

    // Find the opposite variant in the same family
    const targetMode = currentTheme.isDark ? 'light' : 'dark'
    const targetPreset = `${currentTheme.family}-${targetMode}` as ThemePreset

    if (THEME_PRESETS[targetPreset]) {
        return targetPreset
    }

    return currentPreset
}

// Get unique theme families for dropdown (one entry per family)
export function getThemeFamilies(): Array<{ family: string; name: string; lightPreset: ThemePreset; darkPreset: ThemePreset }> {
    const families = new Map<string, { name: string; lightPreset: ThemePreset; darkPreset: ThemePreset }>()

    Object.entries(THEME_PRESETS).forEach(([, theme]) => {
        if (!families.has(theme.family)) {
            families.set(theme.family, {
                name: theme.name,
                lightPreset: `${theme.family}-light` as ThemePreset,
                darkPreset: `${theme.family}-dark` as ThemePreset
            })
        }
    })

    return Array.from(families.entries()).map(([family, data]) => ({
        family,
        ...data
    }))
}

// ============================================
// Custom Themes Management
// ============================================

const CUSTOM_THEMES_KEY = 'zayit-custom-themes'
let customThemes: Record<string, Theme> = {}

// Load custom themes from localStorage
export function loadCustomThemes(): void {
    try {
        const stored = localStorage.getItem(CUSTOM_THEMES_KEY)
        if (stored) {
            customThemes = JSON.parse(stored)
        }
    } catch (e) {
        console.error('Failed to load custom themes:', e)
        customThemes = {}
    }
}

// Save custom themes to localStorage
function saveCustomThemes(): void {
    try {
        localStorage.setItem(CUSTOM_THEMES_KEY, JSON.stringify(customThemes))
    } catch (e) {
        console.error('Failed to save custom themes:', e)
    }
}

// Add a new custom theme
export function addCustomTheme(id: string, theme: Theme): void {
    customThemes[id] = theme
    saveCustomThemes()
}

// Delete a custom theme
export function deleteCustomTheme(id: string): void {
    delete customThemes[id]
    saveCustomThemes()
}

// Get a theme (built-in or custom)
export function getTheme(preset: ThemePreset): Theme | undefined {
    return THEME_PRESETS[preset] || customThemes[preset]
}

// Get all themes (built-in + custom)
export function getAllThemes(): Record<string, Theme> {
    return { ...THEME_PRESETS, ...customThemes }
}

// Get only custom themes
export function getCustomThemes(): Record<string, Theme> {
    return { ...customThemes }
}

// Check if a theme is custom
export function isCustomTheme(preset: ThemePreset): boolean {
    return preset in customThemes
}

// Initialize custom themes on module load
loadCustomThemes()

// ============================================
// PDF Page Filters Control
// ============================================

export function setPdfPageFilters(enabled: boolean): void {
    document.documentElement.setAttribute('data-pdf-filters', enabled ? 'true' : 'false')
    // Sync with all PDF iframes
    syncPdfViewerTheme()
}

// ============================================
// PDF.js Theme Syncing
// ============================================

export function initTheme(): void {
    syncPdfViewerTheme()
    setupPdfViewerThemeObserver()

    if (typeof window !== 'undefined') {
        (window as any).zayitTheme = {
            sync: forceSyncAllPdfViewers,
            isDark: isDarkTheme,
            current: () => isDarkTheme() ? 'dark' : 'light'
        }
    }
}

export function isDarkTheme(): boolean {
    return document.documentElement.classList.contains('dark')
}

export function forceSyncAllPdfViewers(): void {
    console.log('[Theme] Force syncing all PDF viewers...')
    syncPdfViewerTheme()
    setTimeout(() => syncPdfViewerTheme(), 500)
}

export function syncPdfViewerTheme(): void {
    const isDark = isDarkTheme()
    const pdfIframes = document.querySelectorAll('iframe[src*="/pdfjs/web/viewer.html"]')

    pdfIframes.forEach((iframe, index) => {
        try {
            const iframeWindow = (iframe as HTMLIFrameElement).contentWindow
            if (iframeWindow && (iframeWindow as any).PDFViewerApplicationOptions) {
                const AppOptions = (iframeWindow as any).PDFViewerApplicationOptions
                const themeValue = isDark ? 2 : 1
                AppOptions.set('viewerCssTheme', themeValue)

                const iframeDoc = iframeWindow.document
                if (iframeDoc?.documentElement) {
                    // Set color-scheme for native light-dark() support
                    iframeDoc.documentElement.style.setProperty("color-scheme", isDark ? "dark" : "light")

                    // Add/remove dark class for scrollbar theming
                    if (isDark) {
                        iframeDoc.documentElement.classList.add('dark')
                    } else {
                        iframeDoc.documentElement.classList.remove('dark')
                    }

                    // Inject Zayit theme CSS variables into PDF.js iframe
                    const rootStyle = document.documentElement.style
                    const iframeRootStyle = iframeDoc.documentElement.style

                    // Set theme family and PDF filter variable
                    const currentThemePreset = document.documentElement.getAttribute('data-theme-preset')
                    if (currentThemePreset) {
                        const theme = getTheme(currentThemePreset as ThemePreset)

                        // Set theme family attribute
                        if (isCustomTheme(currentThemePreset as ThemePreset)) {
                            iframeDoc.documentElement.setAttribute('data-theme-family', 'custom')
                        } else {
                            const themeFamily = currentThemePreset.split('-')[0]
                            if (themeFamily) {
                                iframeDoc.documentElement.setAttribute('data-theme-family', themeFamily)
                            }
                        }

                        // Set PDF filter as CSS variable (for all themes)
                        if (theme?.pdfFilter) {
                            // Use explicitly defined filter
                            iframeRootStyle.setProperty('--pdf-page-filter', theme.pdfFilter)
                        } else if (theme) {
                            // Auto-calculate filter based on theme colors
                            const autoFilter = calculatePdfFilter(theme)
                            iframeRootStyle.setProperty('--pdf-page-filter', autoFilter)
                        } else {
                            iframeRootStyle.removeProperty('--pdf-page-filter')
                        }
                    }

                    // Set PDF filters enabled/disabled based on parent document
                    const pdfFiltersEnabled = document.documentElement.getAttribute('data-pdf-filters')
                    if (pdfFiltersEnabled) {
                        iframeDoc.documentElement.setAttribute('data-pdf-filters', pdfFiltersEnabled)
                    }

                    // Copy all theme variables from parent to iframe
                    const themeVars = [
                        '--bg-primary-custom',
                        '--bg-secondary-custom',
                        '--text-primary-custom',
                        '--text-secondary-custom',
                        '--border-color-custom',
                        '--accent-color-custom',
                        '--hover-bg-custom',
                        '--active-bg-custom'
                    ]

                    themeVars.forEach(varName => {
                        const value = rootStyle.getPropertyValue(varName)
                        if (value) {
                            iframeRootStyle.setProperty(varName, value)
                        }
                    })
                }
            }
        } catch (error) {
            console.warn(`[Theme] Could not access PDF iframe ${index + 1}:`, error)
        }
    })
}

function setupPdfViewerThemeObserver(): void {
    if ((window as any).__pdfThemeObserverSetup) return
        ; (window as any).__pdfThemeObserverSetup = true

    const observer = new MutationObserver((mutations) => {
        mutations.forEach((mutation) => {
            mutation.addedNodes.forEach((node) => {
                if (node.nodeType === Node.ELEMENT_NODE) {
                    const element = node as Element

                    if (element.tagName === 'IFRAME' &&
                        element.getAttribute('src')?.includes('/pdfjs/web/viewer.html')) {
                        const iframe = element as HTMLIFrameElement
                        iframe.addEventListener('load', () => {
                            setTimeout(() => syncPdfViewerTheme(), 500)
                        })
                        setTimeout(() => syncPdfViewerTheme(), 200)
                    }

                    const pdfIframes = element.querySelectorAll?.('iframe[src*="/pdfjs/web/viewer.html"]')
                    if (pdfIframes?.length) {
                        pdfIframes.forEach((iframe) => {
                            const iframeElement = iframe as HTMLIFrameElement
                            iframeElement.addEventListener('load', () => {
                                setTimeout(() => syncPdfViewerTheme(), 500)
                            })
                        })
                        setTimeout(() => syncPdfViewerTheme(), 200)
                    }
                }
            })
        })
    })

    observer.observe(document.body, {
        childList: true,
        subtree: true
    })
}
