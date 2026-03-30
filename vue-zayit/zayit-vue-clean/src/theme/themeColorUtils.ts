import type { ThemeColors } from './themeTypes'

export function hexToRgb(hex: string): string {
  const r = hexToRgbObj(hex)
  return `${r.r}, ${r.g}, ${r.b}`
}

export function hexToRgbObj(hex: string): { r: number; g: number; b: number } {
  const m = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex)
  if (!m || !m[1] || !m[2] || !m[3]) return { r: 255, g: 255, b: 255 }
  return { r: parseInt(m[1], 16), g: parseInt(m[2], 16), b: parseInt(m[3], 16) }
}

export function lighten(color: string, amount: number): string {
  const hex = color.replace('#', '')
  const channels = [0, 2, 4].map((i) =>
    Math.min(255, Math.max(0, parseInt(hex.slice(i, i + 2), 16) + amount)),
  )
  return '#' + channels.map((x) => Math.round(x).toString(16).padStart(2, '0')).join('')
}

export function darken(color: string, amount: number): string {
  return lighten(color, -amount)
}

export function isDarkColor(hex: string): boolean {
  const { r, g, b } = hexToRgbObj(hex)
  return (0.299 * r + 0.587 * g + 0.114 * b) / 255 < 0.5
}

export function adjustAlpha(isDark: boolean) {
  return {
    hover: `rgba(${isDark ? '255, 255, 255' : '0, 0, 0'}, ${isDark ? 0.08 : 0.06})`,
    active: `rgba(${isDark ? '255, 255, 255' : '0, 0, 0'}, ${isDark ? 0.12 : 0.09})`,
  }
}

export function generateThemeColors(
  backgroundColor: string,
  textColor: string,
  accentColor: string,
): ThemeColors {
  const isDark = isDarkColor(backgroundColor)
  const { hover, active } = adjustAlpha(isDark)
  return {
    bgPrimary: isDark ? lighten(backgroundColor, 5) : darken(backgroundColor, 4),
    bgSecondary: isDark ? lighten(backgroundColor, 15) : darken(backgroundColor, 12),
    bgTertiary: isDark ? lighten(backgroundColor, 8) : darken(backgroundColor, 8),
    textPrimary: textColor,
    textSecondary: isDark ? darken(textColor, 40) : lighten(textColor, 60),
    borderColor: isDark ? lighten(backgroundColor, 25) : darken(backgroundColor, 20),
    accentColor,
    hoverBg: hover,
    activeBg: active,
  }
}
