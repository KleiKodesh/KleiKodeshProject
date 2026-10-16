import { detectFontsByCanvas } from './fontsCanvasProbe'

/**
 * Real system font families that can render Hebrew, sorted alphabetically.
 *
 * Fetched from C#: the WebView2 host enumerates in its own process through DirectWrite
 * (`getFonts` host action, Core/Common/HebrewFonts). Never the service. Without a host (plain
 * dev browser) or if the host fails, fontsCanvasProbe is the fallback; it can only confirm fonts
 * its own CANDIDATES list names, so it under-reports, but a plausible subset beats an empty picker.
 *
 * Lives here rather than in utils/ because it is host I/O. A util may not invoke a host action.
 *
 * Cached per session: one promise, started at app launch (App.vue) so every font dropdown is
 * instant on first open. A hard reload clears it because the module is re-evaluated.
 */
/**
 * Fonts the app ships itself (public/fonts/, declared @font-face in main.css).
 *
 * They are always offered, first and in this order, because they are guaranteed
 * present — unlike the enumerated families, which depend on the machine. The OS
 * enumerators cannot see them (DirectWrite reports installed fonts, and a web font
 * is never installed), so listing them here is the only way they reach the picker.
 *
 * A name repeated by the enumerator is de-duplicated in favour of this entry, so a
 * user who ALSO has the font installed system-wide still sees one row, at the top.
 *
 * Ordered te'amim-capable families first, then commentary and display fonts. Every family
 * except Heebo draws the cantillation marks, so anything a reader picks from the head of
 * this list is safe for a hasTeamim book; Noto Rashi Hebrew is for commentary, and Heebo
 * is for headings only, both without te'amim support.
 */
export const BUNDLED_FONTS = [
  'Taamey Frank CLM',
  'Hadasim CLM',
  'Simple CLM',
  'Stam Ashkenaz CLM',
  'Stam Sefarad CLM',
  'Frank Ruehl Libre',
  'Noto Rashi Hebrew',
  'Heebo',
] as const

/** Bundled families first (order preserved), then everything the OS reported, minus duplicates. */
function withBundledFirst(systemFonts: string[]): string[] {
  const bundled = BUNDLED_FONTS as readonly string[]
  return [...bundled, ...systemFonts.filter((f) => !bundled.includes(f))]
}

/** Per-session cache: one promise shared by every FontSelector. See header. */
let _fontsCache: Promise<string[]> | null = null

async function fetchFonts(): Promise<string[]> {
  if (typeof window.__webviewAction === 'function') {
    try {
      const result = (await window.__webviewAction('getFonts')) as { fonts?: string[] }
      if (Array.isArray(result?.fonts) && result.fonts.length > 0) return withBundledFirst(result.fonts)
    } catch {
      // Host enumeration failed — fall through to the canvas probe.
    }
  }
  return withBundledFirst(detectFontsByCanvas())
}

export function detectAvailableFonts(): Promise<string[]> {
  if (!_fontsCache) _fontsCache = fetchFonts()
  return _fontsCache
}
