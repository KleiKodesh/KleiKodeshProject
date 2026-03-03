// Reading Background Configuration
// Reading backgrounds are simply references to theme reading colors
// This allows mix & match: any theme UI + any theme's reading colors

import { getCustomThemes, type ThemePreset } from './themes'

export type ReadingBackgroundPreset =
    | 'default'
    | 'fluent-light'
    | 'fluent-dark'
    | 'sepia-light'
    | 'sepia-dark'
    | 'night-light'
    | 'night-dark'
    | 'gray-light'
    | 'gray-dark'
    | 'warm-light'
    | 'warm-dark'
    | 'cream-light'
    | 'cream-dark'
    | 'beige-light'
    | 'beige-dark'
    | 'paper-light'
    | 'paper-dark'
    | 'green-light'
    | 'green-dark'
    | 'blue-light'
    | 'blue-dark'
    | 'pink-light'
    | 'pink-dark'
    | 'yellow-light'
    | 'yellow-dark'
    | 'catppuccin-light'
    | 'catppuccin-dark'
    | 'tokyo-light'
    | 'tokyo-dark'
    | 'rose-light'
    | 'rose-dark'
    | 'nord-light'
    | 'nord-dark'
    | 'dracula-light'
    | 'dracula-dark'
    | 'everforest-light'
    | 'everforest-dark'
    | string // Allow custom theme IDs

export interface ReadingBackgroundInfo {
    preset: ReadingBackgroundPreset
    name: string
    displayColor: string // For UI preview
}

// Reading background display names (Hebrew)
const READING_BACKGROUND_NAMES: Record<string, string> = {
    'default': 'ברירת מחדל',
    'fluent-light': 'עיצוב זורם בהיר',
    'fluent-dark': 'עיצוב זורם כהה',
    'sepia-light': 'גוון עתיק בהיר',
    'sepia-dark': 'גוון עתיק כהה',
    'night-light': 'מצב לילה בהיר',
    'night-dark': 'מצב לילה כהה',
    'gray-light': 'גווני אפור בהיר',
    'gray-dark': 'גווני אפור כהה',
    'warm-light': 'גוונים חמים בהיר',
    'warm-dark': 'גוונים חמים כהה',
    'cream-light': 'קרם חם בהיר',
    'cream-dark': 'קרם חם כהה',
    'beige-light': "בז' רך בהיר",
    'beige-dark': "בז' רך כהה",
    'paper-light': 'נייר ישן בהיר',
    'paper-dark': 'נייר ישן כהה',
    'green-light': 'ירוק רך בהיר',
    'green-dark': 'ירוק רך כהה',
    'blue-light': 'כחול רך בהיר',
    'blue-dark': 'כחול רך כהה',
    'pink-light': 'ורוד רך בהיר',
    'pink-dark': 'ורוד רך כהה',
    'yellow-light': 'צהוב עדין בהיר',
    'yellow-dark': 'צהוב עדין כהה',
    'catppuccin-light': 'פסטל מודרני בהיר',
    'catppuccin-dark': 'פסטל מודרני כהה',
    'tokyo-light': 'טוקיו נייט בהיר',
    'tokyo-dark': 'טוקיו נייט כהה',
    'rose-light': 'אורן ורדרד בהיר',
    'rose-dark': 'אורן ורדרד כהה',
    'nord-light': 'ארקטי בהיר',
    'nord-dark': 'ארקטי כהה',
    'dracula-light': 'דרקולה בהיר',
    'dracula-dark': 'דרקולה כהה',
    'everforest-light': 'יער ירוק בהיר',
    'everforest-dark': 'יער ירוק כהה'
}

// Display colors for preview (just the primary background color)
const READING_BACKGROUND_COLORS: Record<string, string> = {
    'default': '#ffffff',
    'fluent-light': '#ffffff',
    'fluent-dark': '#1e1e1e',
    'sepia-light': '#f4ecd8',
    'sepia-dark': '#3a2f1f',
    'night-light': '#f5f5f5',
    'night-dark': '#000000',
    'gray-light': '#f0f0f0',
    'gray-dark': '#2a2a2a',
    'warm-light': '#fef9f3',
    'warm-dark': '#2d2520',
    'cream-light': '#FDF6E3',
    'cream-dark': '#2d2520',
    'beige-light': '#F5F5DC',
    'beige-dark': '#2a2a1a',
    'paper-light': '#FAF0E6',
    'paper-dark': '#2a2018',
    'green-light': '#F0F8F0',
    'green-dark': '#1a2a1a',
    'blue-light': '#F0F8FF',
    'blue-dark': '#1a2a3a',
    'pink-light': '#FFF0F5',
    'pink-dark': '#3a1a2a',
    'yellow-light': '#FFFACD',
    'yellow-dark': '#3a3a1a',
    'catppuccin-light': '#eff1f5',
    'catppuccin-dark': '#1e1e2e',
    'tokyo-light': '#e1e2e7',
    'tokyo-dark': '#1a1b26',
    'rose-light': '#faf4ed',
    'rose-dark': '#232136',
    'nord-light': '#eceff4',
    'nord-dark': '#2e3440',
    'dracula-light': '#f8f8f2',
    'dracula-dark': '#282a36',
    'everforest-light': '#fdf6e3',
    'everforest-dark': '#2d353b'
}

// Get all reading backgrounds for dropdown (built-in + custom)
export function getReadingBackgrounds(): ReadingBackgroundInfo[] {
    const builtIn: ReadingBackgroundInfo[] = Object.keys(READING_BACKGROUND_NAMES).map(preset => ({
        preset: preset as ReadingBackgroundPreset,
        name: READING_BACKGROUND_NAMES[preset]!,
        displayColor: READING_BACKGROUND_COLORS[preset]!
    }))

    // Add custom themes as reading backgrounds
    const customThemes = getCustomThemes()
    const custom: ReadingBackgroundInfo[] = Object.entries(customThemes).map(([id, theme]) => ({
        preset: id as ReadingBackgroundPreset,
        name: `${theme.name} (מותאם אישית)`,
        displayColor: theme.reading.bgPrimary
    }))

    return [...builtIn, ...custom]
}
