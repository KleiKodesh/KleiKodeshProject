/**
 * Theme Builder Composable
 * Logic for building and managing custom themes
 */

import { ref, computed, watch, type Ref } from 'vue'
import { getTheme, type ThemePreset, type ThemeColors } from '@/utils/themes'
import type { ReadingBackgroundPreset } from '@/utils/readingBackgrounds'
import {
    lighten,
    darken,
    adjustAlpha,
    isDarkTheme,
    generateDarkVariant,
    generateThemeColors
} from '@/utils/themeColorUtils'

export interface ThemeBuilderColors {
    bg: string
    text: string
    accent: string
    readingBg: string
    readingText: string
}

export function useThemeBuilder() {
    // Basic state
    const themeName = ref('')
    const baseTheme = ref<ThemePreset | ''>('')
    const currentVariant = ref<'light' | 'dark'>('light')
    const mixAndMatch = ref(false)

    // Reading background mode
    const readingMode = ref<'same' | 'preset' | 'custom'>('same')
    const presetReadingBackground = ref<ReadingBackgroundPreset>('default')

    // Colors for auto mode (single set that gets inverted)
    const backgroundColor = ref('#ffffff')
    const textColor = ref('#1f1f1f')
    const accentColor = ref('#0078d4')
    const customReadingBg = ref('#ffffff')
    const customReadingText = ref('#1f1f1f')

    // Colors for mix-and-match mode (separate light/dark)
    const lightColors = ref<ThemeBuilderColors>({
        bg: '#ffffff',
        text: '#1f1f1f',
        accent: '#0078d4',
        readingBg: '#ffffff',
        readingText: '#1f1f1f'
    })

    const darkColors = ref<ThemeBuilderColors>({
        bg: '#1e1e1e',
        text: '#ffffff',
        accent: '#60cdff',
        readingBg: '#1e1e1e',
        readingText: '#ffffff'
    })

    // Load base theme
    const loadBaseTheme = () => {
        if (!baseTheme.value) return
        const theme = getTheme(baseTheme.value)
        if (theme) {
            backgroundColor.value = theme.ui.bgPrimary
            textColor.value = theme.ui.textPrimary
            accentColor.value = theme.ui.accentColor
            customReadingBg.value = theme.reading.bgPrimary
            customReadingText.value = theme.reading.textPrimary
            currentVariant.value = theme.isDark ? 'dark' : 'light'

            // Also update mix-and-match colors
            if (theme.isDark) {
                darkColors.value = {
                    bg: theme.ui.bgPrimary,
                    text: theme.ui.textPrimary,
                    accent: theme.ui.accentColor,
                    readingBg: theme.reading.bgPrimary,
                    readingText: theme.reading.textPrimary
                }
            } else {
                lightColors.value = {
                    bg: theme.ui.bgPrimary,
                    text: theme.ui.textPrimary,
                    accent: theme.ui.accentColor,
                    readingBg: theme.reading.bgPrimary,
                    readingText: theme.reading.textPrimary
                }
            }
        }
    }

    // Computed colors for auto mode
    const computedReadingColors = computed((): ThemeColors => {
        if (readingMode.value === 'preset' && presetReadingBackground.value !== 'default') {
            const bgTheme = getTheme(presetReadingBackground.value as ThemePreset)
            if (bgTheme) return bgTheme.reading
        }

        if (readingMode.value === 'same') {
            return computedUiColors.value
        }

        const isDark = isDarkTheme(customReadingBg.value)
        const alphas = adjustAlpha(isDark)

        return {
            bgPrimary: customReadingBg.value,
            bgSecondary: isDark ? lighten(customReadingBg.value, 10) : darken(customReadingBg.value, 8),
            textPrimary: customReadingText.value,
            textSecondary: isDark ? darken(customReadingText.value, 40) : lighten(customReadingText.value, 60),
            borderColor: isDark ? lighten(customReadingBg.value, 20) : darken(customReadingBg.value, 15),
            accentColor: accentColor.value,
            hoverBg: alphas.hover,
            activeBg: alphas.active
        }
    })

    const computedUiColors = computed((): ThemeColors => {
        return generateThemeColors(backgroundColor.value, textColor.value, accentColor.value)
    })

    // Computed colors for mix-and-match mode
    const mixMatchUiColors = computed((): ThemeColors => {
        const colors = currentVariant.value === 'light' ? lightColors.value : darkColors.value
        return generateThemeColors(colors.bg, colors.text, colors.accent)
    })

    const mixMatchReadingColors = computed((): ThemeColors => {
        const colors = currentVariant.value === 'light' ? lightColors.value : darkColors.value
        const isDark = currentVariant.value === 'dark'
        const alphas = adjustAlpha(isDark)

        const bgColor = readingMode.value === 'same' ? colors.bg : colors.readingBg
        const textColorVal = readingMode.value === 'same' ? colors.text : colors.readingText

        return {
            bgPrimary: bgColor,
            bgSecondary: isDark ? lighten(bgColor, 10) : darken(bgColor, 8),
            textPrimary: textColorVal,
            textSecondary: isDark ? darken(textColorVal, 40) : lighten(textColorVal, 60),
            borderColor: isDark ? lighten(bgColor, 20) : darken(bgColor, 15),
            accentColor: colors.accent,
            hoverBg: alphas.hover,
            activeBg: alphas.active
        }
    })

    // Light and dark variant colors for preview
    const lightUiVariantColors = computed(() => {
        if (mixAndMatch.value) {
            return generateThemeColors(lightColors.value.bg, lightColors.value.text, lightColors.value.accent)
        }
        // In auto mode, user is editing the light variant
        return computedUiColors.value
    })

    const darkUiVariantColors = computed(() => {
        if (mixAndMatch.value) {
            return generateThemeColors(darkColors.value.bg, darkColors.value.text, darkColors.value.accent)
        }
        // In auto mode, generate dark from light
        return generateDarkVariant(computedUiColors.value)
    })

    const lightVariantColors = computed(() => {
        if (mixAndMatch.value) {
            const isDark = false
            const alphas = adjustAlpha(isDark)
            const bgColor = readingMode.value === 'same' ? lightColors.value.bg : lightColors.value.readingBg
            const textColorVal = readingMode.value === 'same' ? lightColors.value.text : lightColors.value.readingText

            return {
                bgPrimary: bgColor,
                bgSecondary: darken(bgColor, 8),
                bgTertiary: darken(bgColor, 6),
                textPrimary: textColorVal,
                textSecondary: lighten(textColorVal, 60),
                borderColor: darken(bgColor, 15),
                accentColor: lightColors.value.accent,
                hoverBg: alphas.hover,
                activeBg: alphas.active
            }
        }
        // In auto mode, user is editing the light variant
        return computedReadingColors.value
    })

    const darkVariantColors = computed(() => {
        if (mixAndMatch.value) {
            const isDark = true
            const alphas = adjustAlpha(isDark)
            const bgColor = readingMode.value === 'same' ? darkColors.value.bg : darkColors.value.readingBg
            const textColorVal = readingMode.value === 'same' ? darkColors.value.text : darkColors.value.readingText

            return {
                bgPrimary: bgColor,
                bgSecondary: lighten(bgColor, 10),
                bgTertiary: lighten(bgColor, 6),
                textPrimary: textColorVal,
                textSecondary: darken(textColorVal, 40),
                borderColor: lighten(bgColor, 20),
                accentColor: darkColors.value.accent,
                hoverBg: alphas.hover,
                activeBg: alphas.active
            }
        }
        // In auto mode, generate dark from light
        return generateDarkVariant(computedReadingColors.value)
    })

    // Load variant colors into form
    const loadLightVariant = () => {
        currentVariant.value = 'light'
        if (mixAndMatch.value) {
            backgroundColor.value = lightColors.value.bg
            textColor.value = lightColors.value.text
            accentColor.value = lightColors.value.accent
            if (readingMode.value === 'custom') {
                customReadingBg.value = lightColors.value.readingBg
                customReadingText.value = lightColors.value.readingText
            }
        } else {
            const colors = lightVariantColors.value
            backgroundColor.value = colors.bgPrimary
            textColor.value = colors.textPrimary
            accentColor.value = colors.accentColor
            if (readingMode.value === 'custom') {
                customReadingBg.value = colors.bgPrimary
                customReadingText.value = colors.textPrimary
            }
        }
    }

    const loadDarkVariant = () => {
        currentVariant.value = 'dark'
        if (mixAndMatch.value) {
            backgroundColor.value = darkColors.value.bg
            textColor.value = darkColors.value.text
            accentColor.value = darkColors.value.accent
            if (readingMode.value === 'custom') {
                customReadingBg.value = darkColors.value.readingBg
                customReadingText.value = darkColors.value.readingText
            }
        } else {
            const colors = darkVariantColors.value
            backgroundColor.value = colors.bgPrimary
            textColor.value = colors.textPrimary
            accentColor.value = colors.accentColor
            if (readingMode.value === 'custom') {
                customReadingBg.value = colors.bgPrimary
                customReadingText.value = colors.textPrimary
            }
        }
    }

    // Watch color changes and save to mix-and-match storage
    watch([backgroundColor, textColor, accentColor, customReadingBg, customReadingText], () => {
        if (mixAndMatch.value) {
            if (currentVariant.value === 'light') {
                lightColors.value = {
                    bg: backgroundColor.value,
                    text: textColor.value,
                    accent: accentColor.value,
                    readingBg: customReadingBg.value,
                    readingText: customReadingText.value
                }
            } else {
                darkColors.value = {
                    bg: backgroundColor.value,
                    text: textColor.value,
                    accent: accentColor.value,
                    readingBg: customReadingBg.value,
                    readingText: customReadingText.value
                }
            }
        }
    })

    // Watch reading mode changes
    watch(readingMode, (newMode) => {
        if (newMode === 'custom') {
            if (presetReadingBackground.value !== 'default') {
                const bgTheme = getTheme(presetReadingBackground.value as ThemePreset)
                if (bgTheme) {
                    customReadingBg.value = bgTheme.reading.bgPrimary
                    customReadingText.value = bgTheme.reading.textPrimary
                }
            } else {
                customReadingBg.value = backgroundColor.value
                customReadingText.value = textColor.value
            }
        }
    })

    // Build final themes for saving
    const buildThemes = () => {
        const timestamp = Date.now()
        const baseName = themeName.value.trim()

        if (mixAndMatch.value) {
            // Mix-and-match mode: use separate colors
            const lightReadingBg = readingMode.value === 'same' ? lightColors.value.bg : lightColors.value.readingBg
            const lightReadingText = readingMode.value === 'same' ? lightColors.value.text : lightColors.value.readingText
            const darkReadingBg = readingMode.value === 'same' ? darkColors.value.bg : darkColors.value.readingBg
            const darkReadingText = readingMode.value === 'same' ? darkColors.value.text : darkColors.value.readingText

            const lightTheme = {
                id: `custom-${timestamp}-light`,
                name: baseName,
                isDark: false,
                ui: generateThemeColors(lightColors.value.bg, lightColors.value.text, lightColors.value.accent),
                reading: generateThemeColors(lightReadingBg, lightReadingText, lightColors.value.accent)
            }

            const darkTheme = {
                id: `custom-${timestamp}-dark`,
                name: baseName,
                isDark: true,
                ui: generateThemeColors(darkColors.value.bg, darkColors.value.text, darkColors.value.accent),
                reading: generateThemeColors(darkReadingBg, darkReadingText, darkColors.value.accent)
            }

            return [lightTheme, darkTheme]
        } else {
            // Auto mode: user edits light, dark is generated
            const uiColors = computedUiColors.value
            const readingColors = computedReadingColors.value

            const lightTheme = {
                id: `custom-${timestamp}-light`,
                name: baseName,
                isDark: false,
                ui: uiColors,
                reading: readingColors
            }

            const darkTheme = {
                id: `custom-${timestamp}-dark`,
                name: baseName,
                isDark: true,
                ui: generateDarkVariant(uiColors),
                reading: generateDarkVariant(readingColors)
            }

            return [lightTheme, darkTheme]
        }
    }

    return {
        // State
        themeName,
        baseTheme,
        currentVariant,
        mixAndMatch,
        readingMode,
        presetReadingBackground,
        backgroundColor,
        textColor,
        accentColor,
        customReadingBg,
        customReadingText,
        lightColors,
        darkColors,

        // Computed
        lightUiVariantColors,
        darkUiVariantColors,
        lightVariantColors,
        darkVariantColors,

        // Methods
        loadBaseTheme,
        loadLightVariant,
        loadDarkVariant,
        buildThemes
    }
}
