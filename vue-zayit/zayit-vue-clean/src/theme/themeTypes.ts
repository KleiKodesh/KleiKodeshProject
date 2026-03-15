import themesData from './themes.json'

export type ThemePreset = keyof typeof themesData | (string & {})

export interface ThemeColors {
    bgPrimary: string
    bgSecondary: string
    bgTertiary?: string
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
    pdfFilter?: string
}
