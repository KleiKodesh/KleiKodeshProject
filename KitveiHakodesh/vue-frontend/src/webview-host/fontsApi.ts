import { serviceCall } from './serviceClient'
import { detectFontsByCanvas } from './fontsCanvasProbe'

/**
 * Real system font families that can render Hebrew, sorted alphabetically.
 *
 * Both modes ask the OS, so the picker offers the machine's ACTUAL Hebrew fonts:
 *   Hosted: the C# host enumerates via DirectWrite (`getFonts` → Helpers/FontsProvider).
 *   Dev: the KitveiHakodesh service enumerates the same way (`getFonts` →
 *        HebrewFontsProvider — the two providers are TWIN files).
 *
 * fontsCanvasProbe is the last resort when neither is reachable. It can only confirm fonts its
 * own CANDIDATES list names, so it under-reports — but a plausible subset beats an empty picker,
 * and the two real enumerators are what normally answer.
 *
 * Lives here rather than in utils/ because it is entirely host I/O: it talks to the C# bridge or
 * the service and knows which of the two to ask. A util may not invoke a host action.
 *
 * NOTE: `isHosted` is TRUE in dev, so it cannot pick the path — branch on __webviewAction
 * (present only in the real WebView2 host) and let dev go to the service.
 *
 * Deliberately NOT cached: the selector loads the list live on every dropdown open (showing a
 * loading row meanwhile), so a long-running session always sees the machine's current fonts —
 * installs and removals included — with no cache to invalidate.
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
 * Ordered te'amim-capable families first, then Heebo. Every family above Heebo draws
 * the cantillation marks, so anything a reader picks from the head of this list is
 * safe for a hasTeamim book; Heebo trails because it is a headings sans with none.
 */
export const BUNDLED_FONTS = [
  'Taamey Frank CLM',
  'Hadasim CLM',
  'Simple CLM',
  'Stam Ashkenaz CLM',
  'Stam Sefarad CLM',
  'Heebo',
] as const

/** Bundled families first (order preserved), then everything the OS reported, minus duplicates. */
function withBundledFirst(systemFonts: string[]): string[] {
  const bundled = BUNDLED_FONTS as readonly string[]
  return [...bundled, ...systemFonts.filter((f) => !bundled.includes(f))]
}

export async function detectAvailableFonts(): Promise<string[]> {
  if (typeof window.__webviewAction === 'function') {
    try {
      const result = await window.__webviewAction('getFonts')
      const fonts = (result as { fonts?: string[] }).fonts
      if (Array.isArray(fonts) && fonts.length > 0) return withBundledFirst(fonts)
    } catch {
      // Host bridge unavailable — fall through to the canvas probe.
    }
  } else {
    try {
      const result = await serviceCall<{ fonts?: string[] }>('getFonts')
      if (Array.isArray(result?.fonts) && result.fonts.length > 0) return withBundledFirst(result.fonts)
    } catch {
      // Service unreachable — fall through to the canvas probe.
    }
  }
  return withBundledFirst(await detectFontsByCanvas())
}
