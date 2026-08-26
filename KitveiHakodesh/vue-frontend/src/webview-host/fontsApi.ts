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
 * Fonts the app ships itself (public/fonts/, declared @font-face in main.css), and whether each
 * draws the te'amim.
 *
 * They are always offered, first and in this order, because they are guaranteed
 * present - unlike the enumerated families, which depend on the machine. The OS
 * enumerators cannot see them (DirectWrite reports installed fonts, and a web font
 * is never installed), so listing them here is the only way they reach the picker.
 *
 * A name repeated by the enumerator is de-duplicated in favour of this entry, so a
 * user who ALSO has the font installed system-wide still sees one row, at the top.
 *
 * `teamim` is hand-recorded rather than probed because the browser cannot read a cmap:
 * every family below draws all 31 cantillation marks except Heebo, which is the headings
 * sans and draws none. Adding a bundled font means answering this for it too.
 */
const BUNDLED = [
  { name: 'Taamey Frank CLM', teamim: true },
  { name: 'Hadasim CLM', teamim: true },
  { name: 'Simple CLM', teamim: true },
  { name: 'Stam Ashkenaz CLM', teamim: true },
  { name: 'Stam Sefarad CLM', teamim: true },
  { name: 'Heebo', teamim: false },
] as const

export const BUNDLED_FONTS: readonly string[] = BUNDLED.map((f) => f.name)
const BUNDLED_TEAMIM_FONTS: readonly string[] = BUNDLED.filter((f) => f.teamim).map((f) => f.name)

/**
 * The picker's two lists. `fonts` is every family that can render Hebrew; `teamimFonts` is the
 * subset that also draws the cantillation marks, which is all the te'amim picker may offer -
 * a family without them renders a hasTeamim book with the marks dropped.
 */
export interface AvailableFonts {
  fonts: string[]
  teamimFonts: string[]
}

/**
 * `prepend` first (order preserved), then everything the OS reported, minus duplicates.
 *
 * The system list is filtered against EVERY bundled name, not just the prepended ones. CSS
 * resolves a bundled name to the bundled @font-face, so for any name the app ships, the bundled
 * face is what the reader actually gets and the bundled answer is the only true one. Heebo is
 * where that matters: it is bundled WITHOUT te'amim, so a machine-installed Heebo that did carry
 * them must still stay out of the te'amim list -- the reader would get the bundled face regardless.
 */
function withBundledFirst(prepend: readonly string[], systemFonts: string[]): string[] {
  return [...prepend, ...systemFonts.filter((f) => !BUNDLED_FONTS.includes(f))]
}

function combine(fonts: string[], teamimFonts: string[]): AvailableFonts {
  return {
    fonts: withBundledFirst(BUNDLED_FONTS, fonts),
    teamimFonts: withBundledFirst(BUNDLED_TEAMIM_FONTS, teamimFonts),
  }
}

export async function detectAvailableFonts(): Promise<AvailableFonts> {
  if (typeof window.__webviewAction === 'function') {
    try {
      const result = (await window.__webviewAction('getFonts')) as {
        fonts?: string[]
        teamimFonts?: string[]
      }
      if (Array.isArray(result?.fonts) && result.fonts.length > 0) {
        return combine(result.fonts, result.teamimFonts ?? [])
      }
    } catch {
      // Host bridge unavailable - fall through to the canvas probe.
    }
  } else {
    try {
      const result = await serviceCall<{ fonts?: string[]; teamimFonts?: string[] }>('getFonts')
      if (Array.isArray(result?.fonts) && result.fonts.length > 0) {
        return combine(result.fonts, result.teamimFonts ?? [])
      }
    } catch {
      // Service unreachable - fall through to the canvas probe.
    }
  }
  // The canvas probe can confirm a family EXISTS but not what its cmap covers, so it can say
  // nothing about te'amim. The bundled faces are the whole te'amim list here: offering an
  // unverified system font would be guessing on the one question this picker exists to answer.
  return combine(await detectFontsByCanvas(), [])
}
