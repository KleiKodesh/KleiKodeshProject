/**
 * Highlight color palette matching Zayit's HighlightColors object.
 * Values are stored as signed 32-bit integers in SQLite (Kotlin Int is signed).
 * Use `colorArgb >>> 0` to get the unsigned bit pattern before extracting RGB channels.
 */

// Signed Int32 values as stored in user_highlights.colorArgb
export const HIGHLIGHT_COLORS = {
  YELLOW: -5317,       // 0xFFFFEB3B  rgb(255, 235, 59)
  GREEN:  -11751600,   // 0xFF4CAF50  rgb(76, 175, 80)
  BLUE:   -14575885,   // 0xFF2196F3  rgb(33, 150, 243)
  PINK:   -1499549,    // 0xFFE91E63  rgb(233, 30, 99)
  ORANGE: -26624,      // 0xFFFF9800  rgb(255, 152, 0)
} as const

// Ordered list matching Zayit's display order: Orange, Pink, Blue, Green, Yellow
export const HIGHLIGHT_COLORS_LIST: number[] = [
  HIGHLIGHT_COLORS.ORANGE,
  HIGHLIGHT_COLORS.PINK,
  HIGHLIGHT_COLORS.BLUE,
  HIGHLIGHT_COLORS.GREEN,
  HIGHLIGHT_COLORS.YELLOW,
]

/**
 * Theme-adjusted highlight colors for display.
 * The Material Design colors in the DB are bright and don't fit the VSCode/Fluent design.
 * These softened variants preserve the original hue and brightness but reduce saturation
 * to work well with dark/light themes and text readability.
 * The DB stores the original Material colors unchanged — this translator only affects UI rendering.
 */
const THEME_ADJUSTED_HIGHLIGHTS = {
  YELLOW:  'rgba(255, 235, 100, 0.35)',  // Material 0xFFFFEB3B → softened bright yellow
  GREEN:   'rgba(120, 200, 120, 0.35)',  // Material 0xFF4CAF50 → softened bright green
  BLUE:    'rgba(120, 180, 255, 0.35)',  // Material 0xFF2196F3 → softened bright blue
  PINK:    'rgba(240, 150, 200, 0.35)',  // Material 0xFFE91E63 → softened bright pink
  ORANGE:  'rgba(255, 180, 100, 0.35)',  // Material 0xFFFF9800 → softened bright orange
} as const

/** Converts a signed ARGB integer (as stored in SQLite) to a CSS rgb() string. */
export function argbToCssColor(signedArgb: number): string {
  const unsigned = signedArgb >>> 0
  const r = (unsigned >>> 16) & 0xff
  const g = (unsigned >>> 8) & 0xff
  const b = unsigned & 0xff
  return `rgb(${r}, ${g}, ${b})`
}

/**
 * Converts a stored highlight color to a theme-friendly display color.
 * Takes the original Material Design color value (from DB) and returns a muted
 * version that fits the Fluent design system.
 *
 * Why: The original colors are bright and saturated (Material Design palette),
 * which clashes with the subtle VSCode/Fluent color scheme. This function maintains
 * the color identity (same hue, user recognizes yellow/blue/etc.) while reducing
 * saturation and brightness so highlights feel like part of the design, not overlaid.
 *
 * The DB value is never modified — this is a rendering-only translation.
 */
export function highlightColorToThemeColor(signedArgb: number): string {
  const colorKey = Object.entries(HIGHLIGHT_COLORS).find(
    ([, value]) => value === signedArgb
  )?.[0] as keyof typeof THEME_ADJUSTED_HIGHLIGHTS | undefined

  if (colorKey && colorKey in THEME_ADJUSTED_HIGHLIGHTS) {
    return THEME_ADJUSTED_HIGHLIGHTS[colorKey]
  }

  // Fallback for unrecognized colors: render with reduced saturation
  const unsigned = signedArgb >>> 0
  const r = (unsigned >>> 16) & 0xff
  const g = (unsigned >>> 8) & 0xff
  const b = unsigned & 0xff
  // Desaturate by averaging towards middle gray, then apply semi-transparency
  const gray = Math.round((r + g + b) / 3)
  const desatR = Math.round((r + gray) / 2)
  const desatG = Math.round((g + gray) / 2)
  const desatB = Math.round((b + gray) / 2)
  return `rgba(${desatR}, ${desatG}, ${desatB}, 0.25)`
}

/** Converts an unsigned ARGB hex number (e.g. 0xFFFFEB3B) to signed Int32 for storage. */
export function unsignedArgbToSigned(unsigned: number): number {
  return unsigned | 0
}
