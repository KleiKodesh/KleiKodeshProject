/**
 * Theme Color Utilities
 * Pure functions for color manipulation and theme color generation
 */

import type { ThemeColors } from './themes'

// Helper function to convert hex to RGB string
export function hexToRgb(hex: string): string {
    const result = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex)
    if (!result || !result[1] || !result[2] || !result[3]) return '255, 255, 255'
    return `${parseInt(result[1], 16)}, ${parseInt(result[2], 16)}, ${parseInt(result[3], 16)}`
}

// Helper function to convert hex to RGB object
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

// Helper functions to lighten/darken colors
export function lighten(color: string, amount: number): string {
    const hex = color.replace('#', '')
    const r = Math.min(255, parseInt(hex.substring(0, 2), 16) + amount)
    const g = Math.min(255, parseInt(hex.substring(2, 4), 16) + amount)
    const b = Math.min(255, parseInt(hex.substring(4, 6), 16) + amount)
    return '#' + [r, g, b].map(x => Math.round(x).toString(16).padStart(2, '0')).join('')
}

export function darken(color: string, amount: number): string {
    return lighten(color, -amount)
}

export function adjustAlpha(isDark: boolean): { hover: string; active: string } {
    const baseAlpha = isDark ? 0.08 : 0.06
    const activeAlpha = isDark ? 0.12 : 0.09
    return {
        hover: `rgba(${isDark ? '255, 255, 255' : '0, 0, 0'}, ${baseAlpha})`,
        active: `rgba(${isDark ? '255, 255, 255' : '0, 0, 0'}, ${activeAlpha})`
    }
}

// Auto-detect if theme is dark
export function isDarkTheme(bgColor: string): boolean {
    const hex = bgColor.replace('#', '')
    const r = parseInt(hex.substring(0, 2), 16)
    const g = parseInt(hex.substring(2, 4), 16)
    const b = parseInt(hex.substring(4, 6), 16)
    const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255
    return luminance < 0.5
}

// Generate proper dark variant from light colors
export function generateDarkVariant(lightColors: ThemeColors): ThemeColors {
    // Strategy: Maintain color relationships while adapting for dark mode
    // 1. Keep the same hue/warmth as the light theme
    // 2. Use dark backgrounds with similar saturation
    // 3. Use light text
    // 4. Brighten accent color

    const lightBg = lightColors.bgPrimary
    const lightText = lightColors.textPrimary
    const lightAccent = lightColors.accentColor

    // Extract RGB from light background
    const bgHex = lightBg.replace('#', '')
    const bgR = parseInt(bgHex.substring(0, 2), 16) / 255
    const bgG = parseInt(bgHex.substring(2, 4), 16) / 255
    const bgB = parseInt(bgHex.substring(4, 6), 16) / 255

    // Convert to HSL to preserve hue and saturation
    const max = Math.max(bgR, bgG, bgB)
    const min = Math.min(bgR, bgG, bgB)
    const l = (max + min) / 2
    const delta = max - min

    let h = 0
    let s = 0

    if (delta !== 0) {
        s = l > 0.5 ? delta / (2 - max - min) : delta / (max + min)

        if (max === bgR) {
            h = ((bgG - bgB) / delta + (bgG < bgB ? 6 : 0)) / 6
        } else if (max === bgG) {
            h = ((bgB - bgR) / delta + 2) / 6
        } else {
            h = ((bgR - bgG) / delta + 4) / 6
        }
    }

    // Generate dark background with same hue and saturation, but low lightness
    const darkBgPrimary = hslToHex(h, s * 0.8, 0.12)  // Keep some saturation
    const darkBgSecondary = hslToHex(h, s * 0.8, 0.18)
    const darkBgTertiary = hslToHex(h, s * 0.8, 0.15)  // Between primary and secondary
    const darkBorder = hslToHex(h, s * 0.6, 0.25)

    // For text: use light colors
    // If original text was very dark, make it very light
    const textHex = lightText.replace('#', '')
    const textR = parseInt(textHex.substring(0, 2), 16) / 255
    const textG = parseInt(textHex.substring(2, 4), 16) / 255
    const textB = parseInt(textHex.substring(4, 6), 16) / 255
    const textLuminance = 0.299 * textR + 0.587 * textG + 0.114 * textB

    // Generate light text (inverse luminance)
    const darkTextLuminance = 1 - textLuminance
    const darkTextPrimary = hslToHex(0, 0, Math.max(0.85, darkTextLuminance))
    const darkTextSecondary = hslToHex(0, 0, Math.max(0.65, darkTextLuminance * 0.7))

    // For accent: brighten it significantly for dark mode
    const accentHex = lightAccent.replace('#', '')
    const accentR = parseInt(accentHex.substring(0, 2), 16) / 255
    const accentG = parseInt(accentHex.substring(2, 4), 16) / 255
    const accentB = parseInt(accentHex.substring(4, 6), 16) / 255

    // Convert accent to HSL
    const aMax = Math.max(accentR, accentG, accentB)
    const aMin = Math.min(accentR, accentG, accentB)
    const aL = (aMax + aMin) / 2
    const aDelta = aMax - aMin

    let aH = 0
    let aS = 0

    if (aDelta !== 0) {
        aS = aL > 0.5 ? aDelta / (2 - aMax - aMin) : aDelta / (aMax + aMin)

        if (aMax === accentR) {
            aH = ((accentG - accentB) / aDelta + (accentG < accentB ? 6 : 0)) / 6
        } else if (aMax === accentG) {
            aH = ((accentB - accentR) / aDelta + 2) / 6
        } else {
            aH = ((accentR - accentG) / aDelta + 4) / 6
        }
    }

    // Brighten accent for dark mode (increase lightness, keep hue and saturation)
    const darkAccent = hslToHex(aH, Math.min(1, aS * 1.2), Math.min(0.75, aL + 0.3))

    const isDark = true
    const alphas = adjustAlpha(isDark)

    return {
        bgPrimary: darkBgPrimary,
        bgSecondary: darkBgSecondary,
        bgTertiary: darkBgTertiary,
        textPrimary: darkTextPrimary,
        textSecondary: darkTextSecondary,
        borderColor: darkBorder,
        accentColor: darkAccent,
        hoverBg: alphas.hover,
        activeBg: alphas.active
    }
}

// Helper to convert HSL to hex
function hslToHex(h: number, s: number, l: number): string {
    const hueToRgb = (p: number, q: number, t: number) => {
        if (t < 0) t += 1
        if (t > 1) t -= 1
        if (t < 1 / 6) return p + (q - p) * 6 * t
        if (t < 1 / 2) return q
        if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6
        return p
    }

    const q = l < 0.5 ? l * (1 + s) : l + s - l * s
    const p = 2 * l - q

    const r = Math.round(hueToRgb(p, q, h + 1 / 3) * 255)
    const g = Math.round(hueToRgb(p, q, h) * 255)
    const b = Math.round(hueToRgb(p, q, h - 1 / 3) * 255)

    return '#' + [r, g, b].map(x => Math.max(0, Math.min(255, x)).toString(16).padStart(2, '0')).join('')
}


// Generate theme colors from base colors
export function generateThemeColors(
    backgroundColor: string,
    textColor: string,
    accentColor: string
): ThemeColors {
    const isDark = isDarkTheme(backgroundColor)
    const alphas = adjustAlpha(isDark)

    const bgPrimary = isDark ? lighten(backgroundColor, 5) : darken(backgroundColor, 4)
    const bgSecondary = isDark ? lighten(backgroundColor, 15) : darken(backgroundColor, 12)
    const bgTertiary = isDark ? lighten(backgroundColor, 8) : darken(backgroundColor, 8)

    return {
        bgPrimary,
        bgSecondary,
        bgTertiary, // Optional but generated for new themes
        textPrimary: textColor,
        textSecondary: isDark ? darken(textColor, 40) : lighten(textColor, 60),
        borderColor: isDark ? lighten(backgroundColor, 25) : darken(backgroundColor, 20),
        accentColor: accentColor,
        hoverBg: alphas.hover,
        activeBg: alphas.active
    }
}
