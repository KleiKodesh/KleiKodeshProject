/**
 * Theme Type Definitions
 */

import themesData from './themes.json'

// Generate ThemePreset type from JSON keys
export type ThemePreset = keyof typeof themesData | string // Allow custom theme IDs

export interface ThemeColors {
    bgPrimary: string
    bgSecondary: string
    textPrimary: string
    textSecondary: string
    borderColor: string
    accentColor: string
    hoverBg: string
    activeBg: string
}

export interface Theme {
    name: string
    isDark: boolean
    family: string
    reading: ThemeColors
    ui: ThemeColors
}
