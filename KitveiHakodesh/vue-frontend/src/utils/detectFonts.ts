import { serviceCall } from '@/webview-host/serviceClient'

const CANDIDATES = [
  // Culmus
  'Frank Ruehl CLM',
  'Taamey Frank CLM',
  'David CLM',
  'Taamey David CLM',
  'Miriam CLM',
  'Miriam Mono CLM',
  'Nachlieli CLM',
  'Hadasim CLM',
  'Keter YG',
  'Taamey Keter YG',
  'Ktav Yad CLM',
  'Shofar',
  'Simple CLM',
  'Aharoni CLM',
  'Aharoni',
  'Drugulin CLM',
  'Ellinia CLM',
  'Rod CLM',
  'Yehuda CLM',
  'Stam Ashkenaz CLM',
  'Stam Sefarad CLM',
  'Taamey Ashkenaz',
  'Caladings CLM',
  // Guttman
  'Guttman Vilna',
  'Guttman Vilna Bold',
  'Guttman Frank',
  'Guttman Frank Bold',
  'Guttman Frnew',
  'Guttman Aharoni',
  'Guttman-Aharoni Bold',
  'Guttman-Aram',
  'Guttman Haim',
  'Guttman Haim-Condensed',
  'Guttman Rashi',
  'Guttman Rashi Bold',
  'Guttman Stam',
  'Guttman Stam1',
  'Guttman Yad',
  'Guttman Yad-Brush',
  'Guttman Yad-Light',
  'Guttman Mantova',
  'Guttman Mantova Bold',
  'Guttman Mantova-Decor',
  'Guttman Drogolin',
  'Guttman Hatzvi',
  'Guttman Kav',
  'Guttman Kav-Light',
  'Guttman Miryam Bold',
  'Guttman Miryam Light',
  'Guttman-CourMir',
  'Guttman Myamfix',
  // Windows built-in Hebrew
  'David',
  'FrankRuehl',
  'Miriam',
  'Miriam Fixed',
  'Narkisim',
  'Gisha',
  'Levenim MT',
  'Rod',
  'Hadassah Friedlaender',
  // General / UI
  'Arial',
  'Arial Unicode MS',
  'Times New Roman',
  'Courier New',
  'Tahoma',
  'Verdana',
  'Segoe UI',
  'Calibri',
  'Cambria',
  'Georgia',
  // Google Fonts Hebrew
  'Heebo',
  'Rubik',
  'Assistant',
  'Frank Ruhl Libre',
  'Miriam Libre',
  'David Libre',
  'Alef',
  'Noto Sans Hebrew',
  'Noto Serif Hebrew',
  'Noto Rashi Hebrew',
  'Suez One',
  'Secular One',
  'Varela Round',
  'Bellefair',
  'Amatic SC',
  'Rubik Mono One',
  'Rubik Dirt',
  'Rubik Bubbles',
  'Rubik Glitch',
  'Rubik Iso',
  'Rubik Puddles',
  'Rubik Storm',
  'Rubik Vinyl',
  'Rubik Wet Paint',
  // Scholarly
  'Ezra SIL',
  'SBL Hebrew',
  'SBL BibLit',
  'Cardo',
  'Gentium Plus',
]

/**
 * Real system font families that can render Hebrew.
 *
 * Both modes ask the OS, so the font picker offers the machine's ACTUAL Hebrew fonts rather than
 * whatever happens to be in the CANDIDATES guess-list:
 *   Hosted: the C# host enumerates via WPF (`getFonts` → Helpers/FontsProvider).
 *   Dev: the KitveiHakodesh service enumerates via DirectWrite (`getFonts` →
 *        HebrewFontsProvider) — WPF is unavailable under native AOT, so it uses the API WPF
 *        itself wraps, with the same "has a glyph for א" test.
 *
 * The canvas probe stays as the fallback for both: it only ever confirms fonts from CANDIDATES,
 * so it misses anything installed that the list doesn't name.
 *
 * NOTE: `isHosted` is TRUE in dev, so it cannot pick the path — branch on __webviewAction
 * (present only in the real WebView2 host) and let dev fall through to the service.
 */
export async function detectAvailableFonts(): Promise<string[]> {
  if (typeof window.__webviewAction === 'function') {
    try {
      const result = await window.__webviewAction('getFonts')
      const fonts = (result as { fonts: string[] }).fonts
      if (Array.isArray(fonts) && fonts.length > 0) return fonts
    } catch {
      // C# action unavailable — fall through to canvas detection
    }
  } else {
    try {
      const result = await serviceCall<{ fonts?: string[] }>('getFonts')
      if (Array.isArray(result?.fonts) && result.fonts.length > 0) return result.fonts
    } catch {
      // Service unreachable — fall through to canvas detection
    }
  }
  return _detectByCanvas()
}

function _detectByCanvas(): string[] {
  const canvas = document.createElement('canvas')
  const ctx = canvas.getContext('2d')
  if (!ctx) return []
  const baseFonts = ['monospace', 'sans-serif', 'serif']
  const test = 'אבגדהוזחטיכלמנסעפצקרשת'
  const baseWidths: Record<string, number> = {}
  for (const b of baseFonts) {
    ctx.font = `72px ${b}`
    baseWidths[b] = ctx.measureText(test).width
  }
  return CANDIDATES.filter((font) =>
    baseFonts.some((b) => {
      ctx.font = `72px '${font}', ${b}`
      return ctx.measureText(test).width !== baseWidths[b]
    }),
  )
}
