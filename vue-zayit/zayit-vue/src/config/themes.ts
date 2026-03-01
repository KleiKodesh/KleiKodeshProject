// Theme Configuration
// Reading-optimized themes inspired by popular reading apps + modern trending themes

export type ThemePreset =
    | 'fluent-light' | 'fluent-dark'
    | 'sepia-light' | 'sepia-dark'
    | 'night-light' | 'night-dark'
    | 'gray-light' | 'gray-dark'
    | 'warm-light' | 'warm-dark'
    | 'cream-light' | 'cream-dark'
    | 'beige-light' | 'beige-dark'
    | 'paper-light' | 'paper-dark'
    | 'green-light' | 'green-dark'
    | 'blue-light' | 'blue-dark'
    | 'pink-light' | 'pink-dark'
    | 'yellow-light' | 'yellow-dark'
    | 'catppuccin-light' | 'catppuccin-dark'
    | 'tokyo-light' | 'tokyo-dark'
    | 'rose-light' | 'rose-dark'
    | 'nord-light' | 'nord-dark'
    | 'dracula-light' | 'dracula-dark'
    | 'everforest-light' | 'everforest-dark'
    | string // Allow custom theme IDs

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
    // Reading area - optimized for text readability
    reading: ThemeColors
    // UI/Chrome area - matches theme but distinct from reading
    ui: ThemeColors
}

export const THEME_PRESETS: Record<ThemePreset, Theme> = {
    // Fluent Design theme - original app theme
    'fluent-light': {
        name: 'עיצוב זורם',
        family: 'fluent',
        isDark: false,
        reading: {
            // Clean white reading surface
            bgPrimary: '#ffffff',
            bgSecondary: '#f8f8f8',
            textPrimary: '#1f1f1f',
            textSecondary: '#5a5a5a',
            borderColor: '#e5e5e5',
            accentColor: '#0078d4',
            hoverBg: 'rgba(0, 0, 0, 0.06)',
            activeBg: 'rgba(0, 0, 0, 0.09)'
        },
        ui: {
            // Same as reading - no separation for default theme
            bgPrimary: '#ffffff',
            bgSecondary: '#f8f8f8',
            textPrimary: '#1f1f1f',
            textSecondary: '#5a5a5a',
            borderColor: '#e5e5e5',
            accentColor: '#0078d4',
            hoverBg: 'rgba(0, 0, 0, 0.06)',
            activeBg: 'rgba(0, 0, 0, 0.09)'
        }
    },
    'fluent-dark': {
        name: 'עיצוב זורם',
        family: 'fluent',
        isDark: true,
        reading: {
            // VS Code dark reading surface
            bgPrimary: '#1e1e1e',
            bgSecondary: '#2d2d2d',
            textPrimary: '#ffffff',
            textSecondary: '#a6a6a6',
            borderColor: '#3b3b3b',
            accentColor: '#60cdff',
            hoverBg: 'rgba(255, 255, 255, 0.08)',
            activeBg: 'rgba(255, 255, 255, 0.12)'
        },
        ui: {
            // Same as reading - no separation for default theme
            bgPrimary: '#1e1e1e',
            bgSecondary: '#2d2d2d',
            textPrimary: '#ffffff',
            textSecondary: '#a6a6a6',
            borderColor: '#3b3b3b',
            accentColor: '#60cdff',
            hoverBg: 'rgba(255, 255, 255, 0.08)',
            activeBg: 'rgba(255, 255, 255, 0.12)'
        }
    },

    // Sepia theme - like Kindle Sepia, Apple Books Sepia
    'sepia-light': {
        name: 'עתיק',
        family: 'sepia',
        isDark: false,
        reading: {
            bgPrimary: '#f4ecd8',
            bgSecondary: '#ebe1c8',
            textPrimary: '#5f4b32',
            textSecondary: '#8b7355',
            borderColor: '#d4c4a8',
            accentColor: '#8b6914',
            hoverBg: 'rgba(95, 75, 50, 0.06)',
            activeBg: 'rgba(95, 75, 50, 0.09)'
        },
        ui: {
            // Slightly cooler/darker than reading area
            bgPrimary: '#e8e0cc',
            bgSecondary: '#ddd5c0',
            textPrimary: '#4a3828',
            textSecondary: '#7a6345',
            borderColor: '#c8b898',
            accentColor: '#8b6914',
            hoverBg: 'rgba(74, 56, 40, 0.08)',
            activeBg: 'rgba(74, 56, 40, 0.12)'
        }
    },
    'sepia-dark': {
        name: 'עתיק',
        family: 'sepia',
        isDark: true,
        reading: {
            bgPrimary: '#3a2f1f',
            bgSecondary: '#4a3f2f',
            textPrimary: '#e8dcc4',
            textSecondary: '#b8a888',
            borderColor: '#5a4f3f',
            accentColor: '#d4a574',
            hoverBg: 'rgba(232, 220, 196, 0.08)',
            activeBg: 'rgba(232, 220, 196, 0.12)'
        },
        ui: {
            // Slightly lighter than reading area
            bgPrimary: '#4a3f2f',
            bgSecondary: '#5a4f3f',
            textPrimary: '#d8cca4',
            textSecondary: '#a89878',
            borderColor: '#6a5f4f',
            accentColor: '#d4a574',
            hoverBg: 'rgba(216, 204, 164, 0.10)',
            activeBg: 'rgba(216, 204, 164, 0.15)'
        }
    },

    // Night theme - like Kindle Black, Apple Books Night
    'night-light': {
        name: 'לילה לבן',
        family: 'night',
        isDark: false,
        reading: {
            bgPrimary: '#f5f5f5',
            bgSecondary: '#ebebeb',
            textPrimary: '#1a1a1a',
            textSecondary: '#666666',
            borderColor: '#d0d0d0',
            accentColor: '#0066cc',
            hoverBg: 'rgba(26, 26, 26, 0.05)',
            activeBg: 'rgba(26, 26, 26, 0.08)'
        },
        ui: {
            // Slightly darker than reading area
            bgPrimary: '#e8e8e8',
            bgSecondary: '#dedede',
            textPrimary: '#0a0a0a',
            textSecondary: '#565656',
            borderColor: '#c0c0c0',
            accentColor: '#0066cc',
            hoverBg: 'rgba(10, 10, 10, 0.08)',
            activeBg: 'rgba(10, 10, 10, 0.12)'
        }
    },
    'night-dark': {
        name: 'מצב לילה',
        family: 'night',
        isDark: true,
        reading: {
            bgPrimary: '#000000',
            bgSecondary: '#1a1a1a',
            textPrimary: '#e0e0e0',
            textSecondary: '#a0a0a0',
            borderColor: '#2a2a2a',
            accentColor: '#4a9eff',
            hoverBg: 'rgba(224, 224, 224, 0.08)',
            activeBg: 'rgba(224, 224, 224, 0.12)'
        },
        ui: {
            // Slightly lighter than reading area
            bgPrimary: '#1a1a1a',
            bgSecondary: '#2a2a2a',
            textPrimary: '#d0d0d0',
            textSecondary: '#909090',
            borderColor: '#3a3a3a',
            accentColor: '#4a9eff',
            hoverBg: 'rgba(208, 208, 208, 0.10)',
            activeBg: 'rgba(208, 208, 208, 0.15)'
        }
    },

    // Gray theme - like Google Play Books Gray
    'gray-light': {
        name: 'גווני אפור',
        family: 'gray',
        isDark: false,
        reading: {
            bgPrimary: '#f0f0f0',
            bgSecondary: '#e5e5e5',
            textPrimary: '#2a2a2a',
            textSecondary: '#6a6a6a',
            borderColor: '#d0d0d0',
            accentColor: '#1a73e8',
            hoverBg: 'rgba(42, 42, 42, 0.05)',
            activeBg: 'rgba(42, 42, 42, 0.08)'
        },
        ui: {
            // Slightly darker than reading area
            bgPrimary: '#e0e0e0',
            bgSecondary: '#d5d5d5',
            textPrimary: '#1a1a1a',
            textSecondary: '#5a5a5a',
            borderColor: '#c0c0c0',
            accentColor: '#1a73e8',
            hoverBg: 'rgba(26, 26, 26, 0.08)',
            activeBg: 'rgba(26, 26, 26, 0.12)'
        }
    },
    'gray-dark': {
        name: 'גווני אפור',
        family: 'gray',
        isDark: true,
        reading: {
            bgPrimary: '#2a2a2a',
            bgSecondary: '#3a3a3a',
            textPrimary: '#e0e0e0',
            textSecondary: '#b0b0b0',
            borderColor: '#4a4a4a',
            accentColor: '#8ab4f8',
            hoverBg: 'rgba(224, 224, 224, 0.08)',
            activeBg: 'rgba(224, 224, 224, 0.12)'
        },
        ui: {
            // Slightly lighter than reading area
            bgPrimary: '#3a3a3a',
            bgSecondary: '#4a4a4a',
            textPrimary: '#d0d0d0',
            textSecondary: '#a0a0a0',
            borderColor: '#5a5a5a',
            accentColor: '#8ab4f8',
            hoverBg: 'rgba(208, 208, 208, 0.10)',
            activeBg: 'rgba(208, 208, 208, 0.15)'
        }
    },

    // Warm theme - like Kobo Warm, comfortable for evening reading
    'warm-light': {
        name: 'גוונים חמים',
        family: 'warm',
        isDark: false,
        reading: {
            bgPrimary: '#fef9f3',
            bgSecondary: '#f5ede3',
            textPrimary: '#3a3028',
            textSecondary: '#6a5a48',
            borderColor: '#e5d9c8',
            accentColor: '#c17817',
            hoverBg: 'rgba(58, 48, 40, 0.05)',
            activeBg: 'rgba(58, 48, 40, 0.08)'
        },
        ui: {
            // Slightly cooler/darker than reading area
            bgPrimary: '#f0e8d8',
            bgSecondary: '#e5ddc8',
            textPrimary: '#2a2018',
            textSecondary: '#5a4a38',
            borderColor: '#d5c9b8',
            accentColor: '#c17817',
            hoverBg: 'rgba(42, 32, 24, 0.08)',
            activeBg: 'rgba(42, 32, 24, 0.12)'
        }
    },
    'warm-dark': {
        name: 'גוונים חמים',
        family: 'warm',
        isDark: true,
        reading: {
            bgPrimary: '#2d2520',
            bgSecondary: '#3d3530',
            textPrimary: '#e8dcc8',
            textSecondary: '#b8a888',
            borderColor: '#4d4540',
            accentColor: '#e8a857',
            hoverBg: 'rgba(232, 220, 200, 0.08)',
            activeBg: 'rgba(232, 220, 200, 0.12)'
        },
        ui: {
            // Slightly lighter than reading area
            bgPrimary: '#3d3530',
            bgSecondary: '#4d4540',
            textPrimary: '#d8ccb8',
            textSecondary: '#a89878',
            borderColor: '#5d5550',
            accentColor: '#e8a857',
            hoverBg: 'rgba(216, 204, 184, 0.10)',
            activeBg: 'rgba(216, 204, 184, 0.15)'
        }
    },

    // Cream theme - Warm cream reading background
    'cream-light': {
        name: 'קרם חם',
        family: 'cream',
        isDark: false,
        reading: {
            bgPrimary: '#FDF6E3',
            bgSecondary: '#F4F0D9',
            textPrimary: '#5c6a72',
            textSecondary: '#829181',
            borderColor: '#e6dfc8',
            accentColor: '#8da101',
            hoverBg: 'rgba(92, 106, 114, 0.06)',
            activeBg: 'rgba(92, 106, 114, 0.09)'
        },
        ui: {
            bgPrimary: '#F0E8D0',
            bgSecondary: '#E6DFC8',
            textPrimary: '#4c5a62',
            textSecondary: '#728171',
            borderColor: '#d6cfa8',
            accentColor: '#8da101',
            hoverBg: 'rgba(76, 90, 98, 0.08)',
            activeBg: 'rgba(76, 90, 98, 0.12)'
        }
    },
    'cream-dark': {
        name: 'קרם חם',
        family: 'cream',
        isDark: true,
        reading: {
            bgPrimary: '#2d2520',
            bgSecondary: '#3d3530',
            textPrimary: '#e8dcc8',
            textSecondary: '#b8a888',
            borderColor: '#4d4540',
            accentColor: '#e8a857',
            hoverBg: 'rgba(232, 220, 200, 0.08)',
            activeBg: 'rgba(232, 220, 200, 0.12)'
        },
        ui: {
            bgPrimary: '#3d3530',
            bgSecondary: '#4d4540',
            textPrimary: '#d8ccb8',
            textSecondary: '#a89878',
            borderColor: '#5d5550',
            accentColor: '#e8a857',
            hoverBg: 'rgba(216, 204, 184, 0.10)',
            activeBg: 'rgba(216, 204, 184, 0.15)'
        }
    },

    // Beige theme - Soft beige reading background
    'beige-light': {
        name: "בז' רך",
        family: 'beige',
        isDark: false,
        reading: {
            bgPrimary: '#F5F5DC',
            bgSecondary: '#EEEECB',
            textPrimary: '#4a4a3a',
            textSecondary: '#6a6a5a',
            borderColor: '#d5d5c2',
            accentColor: '#8b7914',
            hoverBg: 'rgba(74, 74, 58, 0.06)',
            activeBg: 'rgba(74, 74, 58, 0.09)'
        },
        ui: {
            bgPrimary: '#EEEECB',
            bgSecondary: '#E0E0B8',
            textPrimary: '#3a3a2a',
            textSecondary: '#5a5a4a',
            borderColor: '#c5c5b2',
            accentColor: '#8b7914',
            hoverBg: 'rgba(58, 58, 42, 0.08)',
            activeBg: 'rgba(58, 58, 42, 0.12)'
        }
    },
    'beige-dark': {
        name: "בז' רך",
        family: 'beige',
        isDark: true,
        reading: {
            bgPrimary: '#2a2a1a',
            bgSecondary: '#3a3a2a',
            textPrimary: '#e5e5cc',
            textSecondary: '#b5b59c',
            borderColor: '#4a4a3a',
            accentColor: '#c4a574',
            hoverBg: 'rgba(229, 229, 204, 0.08)',
            activeBg: 'rgba(229, 229, 204, 0.12)'
        },
        ui: {
            bgPrimary: '#3a3a2a',
            bgSecondary: '#4a4a3a',
            textPrimary: '#d5d5bc',
            textSecondary: '#a5a58c',
            borderColor: '#5a5a4a',
            accentColor: '#c4a574',
            hoverBg: 'rgba(213, 213, 188, 0.10)',
            activeBg: 'rgba(213, 213, 188, 0.15)'
        }
    },

    // Paper theme - Old paper reading background
    'paper-light': {
        name: 'נייר ישן',
        family: 'paper',
        isDark: false,
        reading: {
            bgPrimary: '#FAF0E6',
            bgSecondary: '#F0E6DC',
            textPrimary: '#3a3028',
            textSecondary: '#6a5a48',
            borderColor: '#e0d6cc',
            accentColor: '#c17817',
            hoverBg: 'rgba(58, 48, 40, 0.05)',
            activeBg: 'rgba(58, 48, 40, 0.08)'
        },
        ui: {
            bgPrimary: '#F0E6DC',
            bgSecondary: '#E6DCD2',
            textPrimary: '#2a2018',
            textSecondary: '#5a4a38',
            borderColor: '#d0c6bc',
            accentColor: '#c17817',
            hoverBg: 'rgba(42, 32, 24, 0.08)',
            activeBg: 'rgba(42, 32, 24, 0.12)'
        }
    },
    'paper-dark': {
        name: 'נייר ישן',
        family: 'paper',
        isDark: true,
        reading: {
            bgPrimary: '#2a2018',
            bgSecondary: '#3a3028',
            textPrimary: '#eae0d6',
            textSecondary: '#baa898',
            borderColor: '#4a4038',
            accentColor: '#e8a857',
            hoverBg: 'rgba(234, 224, 214, 0.08)',
            activeBg: 'rgba(234, 224, 214, 0.12)'
        },
        ui: {
            bgPrimary: '#3a3028',
            bgSecondary: '#4a4038',
            textPrimary: '#dad0c6',
            textSecondary: '#aa9888',
            borderColor: '#5a5048',
            accentColor: '#e8a857',
            hoverBg: 'rgba(218, 208, 198, 0.10)',
            activeBg: 'rgba(218, 208, 198, 0.15)'
        }
    },

    // Green theme - Soft green reading background
    'green-light': {
        name: 'ירוק רך',
        family: 'green',
        isDark: false,
        reading: {
            bgPrimary: '#F0F8F0',
            bgSecondary: '#E6F0E6',
            textPrimary: '#2a3a2a',
            textSecondary: '#4a6a4a',
            borderColor: '#d0e0d0',
            accentColor: '#4a8a4a',
            hoverBg: 'rgba(42, 58, 42, 0.06)',
            activeBg: 'rgba(42, 58, 42, 0.09)'
        },
        ui: {
            bgPrimary: '#E6F0E6',
            bgSecondary: '#DCE6DC',
            textPrimary: '#1a2a1a',
            textSecondary: '#3a5a3a',
            borderColor: '#c0d0c0',
            accentColor: '#4a8a4a',
            hoverBg: 'rgba(26, 42, 26, 0.08)',
            activeBg: 'rgba(26, 42, 26, 0.12)'
        }
    },
    'green-dark': {
        name: 'ירוק רך',
        family: 'green',
        isDark: true,
        reading: {
            bgPrimary: '#1a2a1a',
            bgSecondary: '#2a3a2a',
            textPrimary: '#e0f0e0',
            textSecondary: '#b0c0b0',
            borderColor: '#3a4a3a',
            accentColor: '#7ac07a',
            hoverBg: 'rgba(224, 240, 224, 0.08)',
            activeBg: 'rgba(224, 240, 224, 0.12)'
        },
        ui: {
            bgPrimary: '#2a3a2a',
            bgSecondary: '#3a4a3a',
            textPrimary: '#d0e0d0',
            textSecondary: '#a0b0a0',
            borderColor: '#4a5a4a',
            accentColor: '#7ac07a',
            hoverBg: 'rgba(208, 224, 208, 0.10)',
            activeBg: 'rgba(208, 224, 208, 0.15)'
        }
    },

    // Blue theme - Soft blue reading background
    'blue-light': {
        name: 'כחול רך',
        family: 'blue',
        isDark: false,
        reading: {
            bgPrimary: '#F0F8FF',
            bgSecondary: '#E6EEF8',
            textPrimary: '#2a3a4a',
            textSecondary: '#4a5a6a',
            borderColor: '#d0e0f0',
            accentColor: '#4a7ac0',
            hoverBg: 'rgba(42, 58, 74, 0.06)',
            activeBg: 'rgba(42, 58, 74, 0.09)'
        },
        ui: {
            bgPrimary: '#E6EEF8',
            bgSecondary: '#DCE4F0',
            textPrimary: '#1a2a3a',
            textSecondary: '#3a4a5a',
            borderColor: '#c0d0e0',
            accentColor: '#4a7ac0',
            hoverBg: 'rgba(26, 42, 58, 0.08)',
            activeBg: 'rgba(26, 42, 58, 0.12)'
        }
    },
    'blue-dark': {
        name: 'כחול רך',
        family: 'blue',
        isDark: true,
        reading: {
            bgPrimary: '#1a2a3a',
            bgSecondary: '#2a3a4a',
            textPrimary: '#e0eef8',
            textSecondary: '#b0c0d0',
            borderColor: '#3a4a5a',
            accentColor: '#7ab0f0',
            hoverBg: 'rgba(224, 238, 248, 0.08)',
            activeBg: 'rgba(224, 238, 248, 0.12)'
        },
        ui: {
            bgPrimary: '#2a3a4a',
            bgSecondary: '#3a4a5a',
            textPrimary: '#d0dee8',
            textSecondary: '#a0b0c0',
            borderColor: '#4a5a6a',
            accentColor: '#7ab0f0',
            hoverBg: 'rgba(208, 222, 232, 0.10)',
            activeBg: 'rgba(208, 222, 232, 0.15)'
        }
    },

    // Pink theme - Soft pink reading background
    'pink-light': {
        name: 'ורוד רך',
        family: 'pink',
        isDark: false,
        reading: {
            bgPrimary: '#FFF0F5',
            bgSecondary: '#F8E6EB',
            textPrimary: '#4a2a3a',
            textSecondary: '#6a4a5a',
            borderColor: '#f0d6e0',
            accentColor: '#c04a7a',
            hoverBg: 'rgba(74, 42, 58, 0.06)',
            activeBg: 'rgba(74, 42, 58, 0.09)'
        },
        ui: {
            bgPrimary: '#F8E6EB',
            bgSecondary: '#F0DCE1',
            textPrimary: '#3a1a2a',
            textSecondary: '#5a3a4a',
            borderColor: '#e0c6d0',
            accentColor: '#c04a7a',
            hoverBg: 'rgba(58, 26, 42, 0.08)',
            activeBg: 'rgba(58, 26, 42, 0.12)'
        }
    },
    'pink-dark': {
        name: 'ורוד רך',
        family: 'pink',
        isDark: true,
        reading: {
            bgPrimary: '#3a1a2a',
            bgSecondary: '#4a2a3a',
            textPrimary: '#f8e0eb',
            textSecondary: '#c8b0bb',
            borderColor: '#5a3a4a',
            accentColor: '#f07aa0',
            hoverBg: 'rgba(248, 224, 235, 0.08)',
            activeBg: 'rgba(248, 224, 235, 0.12)'
        },
        ui: {
            bgPrimary: '#4a2a3a',
            bgSecondary: '#5a3a4a',
            textPrimary: '#e8d0db',
            textSecondary: '#b8a0ab',
            borderColor: '#6a4a5a',
            accentColor: '#f07aa0',
            hoverBg: 'rgba(232, 208, 219, 0.10)',
            activeBg: 'rgba(232, 208, 219, 0.15)'
        }
    },

    // Yellow theme - Gentle yellow reading background
    'yellow-light': {
        name: 'צהוב עדין',
        family: 'yellow',
        isDark: false,
        reading: {
            bgPrimary: '#FFFACD',
            bgSecondary: '#F8F0C3',
            textPrimary: '#4a4a2a',
            textSecondary: '#6a6a4a',
            borderColor: '#e8e0b3',
            accentColor: '#a0a040',
            hoverBg: 'rgba(74, 74, 42, 0.06)',
            activeBg: 'rgba(74, 74, 42, 0.09)'
        },
        ui: {
            bgPrimary: '#F8F0C3',
            bgSecondary: '#F0E8B9',
            textPrimary: '#3a3a1a',
            textSecondary: '#5a5a3a',
            borderColor: '#d8d0a3',
            accentColor: '#a0a040',
            hoverBg: 'rgba(58, 58, 26, 0.08)',
            activeBg: 'rgba(58, 58, 26, 0.12)'
        }
    },
    'yellow-dark': {
        name: 'צהוב עדין',
        family: 'yellow',
        isDark: true,
        reading: {
            bgPrimary: '#3a3a1a',
            bgSecondary: '#4a4a2a',
            textPrimary: '#f8f0cd',
            textSecondary: '#c8c09d',
            borderColor: '#5a5a3a',
            accentColor: '#d0d070',
            hoverBg: 'rgba(248, 240, 205, 0.08)',
            activeBg: 'rgba(248, 240, 205, 0.12)'
        },
        ui: {
            bgPrimary: '#4a4a2a',
            bgSecondary: '#5a5a3a',
            textPrimary: '#e8e0bd',
            textSecondary: '#b8b08d',
            borderColor: '#6a6a4a',
            accentColor: '#d0d070',
            hoverBg: 'rgba(232, 224, 189, 0.10)',
            activeBg: 'rgba(232, 224, 189, 0.15)'
        }
    },

    // Catppuccin - Modern, pastel theme (trending on GitHub)
    'catppuccin-light': {
        name: 'פסטל מודרני',
        family: 'catppuccin',
        isDark: false,
        reading: {
            bgPrimary: '#eff1f5',
            bgSecondary: '#e6e9ef',
            textPrimary: '#4c4f69',
            textSecondary: '#6c6f85',
            borderColor: '#dce0e8',
            accentColor: '#1e66f5',
            hoverBg: 'rgba(76, 79, 105, 0.06)',
            activeBg: 'rgba(76, 79, 105, 0.09)'
        },
        ui: {
            // Slightly darker/cooler than reading area
            bgPrimary: '#dce0e8',
            bgSecondary: '#ccd0da',
            textPrimary: '#3c3f59',
            textSecondary: '#5c5f75',
            borderColor: '#bcc0d0',
            accentColor: '#1e66f5',
            hoverBg: 'rgba(60, 63, 89, 0.08)',
            activeBg: 'rgba(60, 63, 89, 0.12)'
        }
    },
    'catppuccin-dark': {
        name: 'פסטל מודרני',
        family: 'catppuccin',
        isDark: true,
        reading: {
            bgPrimary: '#1e1e2e',
            bgSecondary: '#181825',
            textPrimary: '#cdd6f4',
            textSecondary: '#a6adc8',
            borderColor: '#313244',
            accentColor: '#89b4fa',
            hoverBg: 'rgba(205, 214, 244, 0.08)',
            activeBg: 'rgba(205, 214, 244, 0.12)'
        },
        ui: {
            // Slightly lighter than reading area
            bgPrimary: '#313244',
            bgSecondary: '#45475a',
            textPrimary: '#bac2de',
            textSecondary: '#969dbb',
            borderColor: '#585b70',
            accentColor: '#89b4fa',
            hoverBg: 'rgba(186, 194, 222, 0.10)',
            activeBg: 'rgba(186, 194, 222, 0.15)'
        }
    },

    // Tokyo Night - Popular modern theme
    'tokyo-light': {
        name: 'טוקיו נייט',
        family: 'tokyo',
        isDark: false,
        reading: {
            bgPrimary: '#e1e2e7',
            bgSecondary: '#d5d6db',
            textPrimary: '#343b58',
            textSecondary: '#565f89',
            borderColor: '#c0c1c6',
            accentColor: '#2e7de9',
            hoverBg: 'rgba(52, 59, 88, 0.06)',
            activeBg: 'rgba(52, 59, 88, 0.09)'
        },
        ui: {
            // Slightly darker than reading area
            bgPrimary: '#d5d6db',
            bgSecondary: '#c5c6cb',
            textPrimary: '#343b58',
            textSecondary: '#565f89',
            borderColor: '#b0b1b6',
            accentColor: '#2e7de9',
            hoverBg: 'rgba(52, 59, 88, 0.08)',
            activeBg: 'rgba(52, 59, 88, 0.12)'
        }
    },
    'tokyo-dark': {
        name: 'טוקיו נייט',
        family: 'tokyo',
        isDark: true,
        reading: {
            bgPrimary: '#1a1b26',
            bgSecondary: '#16161e',
            textPrimary: '#c0caf5',
            textSecondary: '#565f89',
            borderColor: '#292e42',
            accentColor: '#7aa2f7',
            hoverBg: 'rgba(192, 202, 245, 0.08)',
            activeBg: 'rgba(192, 202, 245, 0.12)'
        },
        ui: {
            // Slightly lighter than reading area
            bgPrimary: '#24283b',
            bgSecondary: '#292e42',
            textPrimary: '#b0bae5',
            textSecondary: '#565f89',
            borderColor: '#3b4261',
            accentColor: '#7aa2f7',
            hoverBg: 'rgba(176, 186, 229, 0.10)',
            activeBg: 'rgba(176, 186, 229, 0.15)'
        }
    },

    // Rosé Pine - Elegant, modern theme
    'rose-light': {
        name: 'אורן ורדרד',
        family: 'rose',
        isDark: false,
        reading: {
            bgPrimary: '#faf4ed',
            bgSecondary: '#f2e9e1',
            textPrimary: '#575279',
            textSecondary: '#797593',
            borderColor: '#dfdad9',
            accentColor: '#d7827e',
            hoverBg: 'rgba(87, 82, 121, 0.06)',
            activeBg: 'rgba(87, 82, 121, 0.09)'
        },
        ui: {
            // Slightly darker/cooler than reading area
            bgPrimary: '#f2e9e1',
            bgSecondary: '#e8dfd7',
            textPrimary: '#474269',
            textSecondary: '#696583',
            borderColor: '#cfcac9',
            accentColor: '#d7827e',
            hoverBg: 'rgba(71, 66, 105, 0.08)',
            activeBg: 'rgba(71, 66, 105, 0.12)'
        }
    },
    'rose-dark': {
        name: 'אורן ורדרד',
        family: 'rose',
        isDark: true,
        reading: {
            bgPrimary: '#232136',
            bgSecondary: '#2a273f',
            textPrimary: '#e0def4',
            textSecondary: '#908caa',
            borderColor: '#393552',
            accentColor: '#ea9a97',
            hoverBg: 'rgba(224, 222, 244, 0.08)',
            activeBg: 'rgba(224, 222, 244, 0.12)'
        },
        ui: {
            // Slightly lighter than reading area
            bgPrimary: '#2a273f',
            bgSecondary: '#393552',
            textPrimary: '#d0cee4',
            textSecondary: '#807c9a',
            borderColor: '#494562',
            accentColor: '#ea9a97',
            hoverBg: 'rgba(208, 206, 228, 0.10)',
            activeBg: 'rgba(208, 206, 228, 0.15)'
        }
    },

    // Nord - Cool, arctic theme
    'nord-light': {
        name: 'ארקטי',
        family: 'nord',
        isDark: false,
        reading: {
            bgPrimary: '#eceff4',
            bgSecondary: '#e5e9f0',
            textPrimary: '#2e3440',
            textSecondary: '#4c566a',
            borderColor: '#d8dee9',
            accentColor: '#5e81ac',
            hoverBg: 'rgba(46, 52, 64, 0.06)',
            activeBg: 'rgba(46, 52, 64, 0.09)'
        },
        ui: {
            // Slightly darker than reading area
            bgPrimary: '#d8dee9',
            bgSecondary: '#c8cee0',
            textPrimary: '#1e2430',
            textSecondary: '#3c465a',
            borderColor: '#b8bed9',
            accentColor: '#5e81ac',
            hoverBg: 'rgba(30, 36, 48, 0.08)',
            activeBg: 'rgba(30, 36, 48, 0.12)'
        }
    },
    'nord-dark': {
        name: 'ארקטי',
        family: 'nord',
        isDark: true,
        reading: {
            bgPrimary: '#2e3440',
            bgSecondary: '#3b4252',
            textPrimary: '#eceff4',
            textSecondary: '#d8dee9',
            borderColor: '#4c566a',
            accentColor: '#88c0d0',
            hoverBg: 'rgba(236, 239, 244, 0.08)',
            activeBg: 'rgba(236, 239, 244, 0.12)'
        },
        ui: {
            // Slightly lighter than reading area
            bgPrimary: '#3b4252',
            bgSecondary: '#434c5e',
            textPrimary: '#dcdfe4',
            textSecondary: '#c8ced9',
            borderColor: '#5c667a',
            accentColor: '#88c0d0',
            hoverBg: 'rgba(220, 223, 228, 0.10)',
            activeBg: 'rgba(220, 223, 228, 0.15)'
        }
    },

    // Dracula - Popular vibrant theme
    'dracula-light': {
        name: 'דרקולה',
        family: 'dracula',
        isDark: false,
        reading: {
            bgPrimary: '#f8f8f2',
            bgSecondary: '#f0f0eb',
            textPrimary: '#282a36',
            textSecondary: '#6272a4',
            borderColor: '#e0e0db',
            accentColor: '#bd93f9',
            hoverBg: 'rgba(40, 42, 54, 0.06)',
            activeBg: 'rgba(40, 42, 54, 0.09)'
        },
        ui: {
            // Slightly darker/cooler than reading area
            bgPrimary: '#e8e8e2',
            bgSecondary: '#d8d8d2',
            textPrimary: '#181a26',
            textSecondary: '#526294',
            borderColor: '#c8c8c2',
            accentColor: '#bd93f9',
            hoverBg: 'rgba(24, 26, 38, 0.08)',
            activeBg: 'rgba(24, 26, 38, 0.12)'
        }
    },
    'dracula-dark': {
        name: 'דרקולה',
        family: 'dracula',
        isDark: true,
        reading: {
            bgPrimary: '#282a36',
            bgSecondary: '#21222c',
            textPrimary: '#f8f8f2',
            textSecondary: '#6272a4',
            borderColor: '#44475a',
            accentColor: '#bd93f9',
            hoverBg: 'rgba(248, 248, 242, 0.08)',
            activeBg: 'rgba(248, 248, 242, 0.12)'
        },
        ui: {
            // Slightly lighter than reading area
            bgPrimary: '#343746',
            bgSecondary: '#44475a',
            textPrimary: '#e8e8e2',
            textSecondary: '#7282b4',
            borderColor: '#54576a',
            accentColor: '#bd93f9',
            hoverBg: 'rgba(232, 232, 226, 0.10)',
            activeBg: 'rgba(232, 232, 226, 0.15)'
        }
    },

    // Everforest - Modern green theme, comfortable for reading
    'everforest-light': {
        name: 'יער ירוק',
        family: 'everforest',
        isDark: false,
        reading: {
            bgPrimary: '#fdf6e3',
            bgSecondary: '#f4f0d9',
            textPrimary: '#5c6a72',
            textSecondary: '#829181',
            borderColor: '#e6dfc8',
            accentColor: '#8da101',
            hoverBg: 'rgba(92, 106, 114, 0.06)',
            activeBg: 'rgba(92, 106, 114, 0.09)'
        },
        ui: {
            // Slightly darker/cooler than reading area
            bgPrimary: '#f0e8d0',
            bgSecondary: '#e6dfc8',
            textPrimary: '#4c5a62',
            textSecondary: '#728171',
            borderColor: '#d6cfa8',
            accentColor: '#8da101',
            hoverBg: 'rgba(76, 90, 98, 0.08)',
            activeBg: 'rgba(76, 90, 98, 0.12)'
        }
    },
    'everforest-dark': {
        name: 'יער ירוק',
        family: 'everforest',
        isDark: true,
        reading: {
            bgPrimary: '#2d353b',
            bgSecondary: '#343f44',
            textPrimary: '#d3c6aa',
            textSecondary: '#859289',
            borderColor: '#414b50',
            accentColor: '#a7c080',
            hoverBg: 'rgba(211, 198, 170, 0.08)',
            activeBg: 'rgba(211, 198, 170, 0.12)'
        },
        ui: {
            // Slightly lighter than reading area
            bgPrimary: '#343f44',
            bgSecondary: '#3d484d',
            textPrimary: '#c3b69a',
            textSecondary: '#758279',
            borderColor: '#4d5860',
            accentColor: '#a7c080',
            hoverBg: 'rgba(195, 182, 154, 0.10)',
            activeBg: 'rgba(195, 182, 154, 0.15)'
        }
    }
}

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

    Object.entries(THEME_PRESETS).forEach(([key, theme]) => {
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


// Custom Themes Management
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
