/**
 * Theme Configuration and Utilities
 * Reading-optimized themes + PDF.js viewer theme syncing
 */

import themesData from '@/data/themes.json'
import type { ThemePreset, Theme } from '@/data/themeTypes'

// Re-export types for convenience
export type { ThemePreset, Theme, ThemeColors } from '@/data/themeTypes'

// Load theme presets from JSON
export const THEME_PRESETS: Record<ThemePreset, Theme> = themesData as Record<ThemePreset, Theme>

// ============================================
// Theme Utilities
// ============================================

// Helper function to convert hex to RGB
export function hexToRgb(hex: string): string {
    const result = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex)
    if (!result || !result[1] || !result[2] || !result[3]) return '255, 255, 255'
    return `${parseInt(result[1], 16)}, ${parseInt(result[2], 16)}, ${parseInt(result[3], 16)}`
}

export function hexToRgbObj(hex: string): { r: number; g: number; b: number } {
    const result = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex)
    if (!result || !result[1] || !result[2] || !result[3]) {
        return { r: 255, g: 255, b: 255 }
    }
    return {
        r: parseInt(result[1], 16),
        g: parseInt(result[2], 16),
        b: parseInt(result[3], 16)
    }
}

// Apply theme to document
export function applyTheme(preset: ThemePreset) {
    const theme = getTheme(preset)
    if (!theme) return

    const uiColors = theme.ui
    const readingColors = theme.reading

    // Set UI CSS custom properties (for general app UI)
    document.documentElement.style.setProperty('--bg-primary-custom', uiColors.bgPrimary)
    document.documentElement.style.setProperty('--bg-secondary-custom', uiColors.bgSecondary)
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
                    iframeDoc.documentElement.style.setProperty("color-scheme", isDark ? "dark" : "light")
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
