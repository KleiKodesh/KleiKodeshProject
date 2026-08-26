/**
 * Last-resort font detection: probe a known list of Hebrew font names by measuring text width.
 *
 * This is the FALLBACK for detectAvailableFonts, used only when the real enumerator is
 * unreachable — the C# host bridge in hosted mode, the KitveiHakodesh service in dev. Both of
 * those ask the OS and return every installed Hebrew family; this file cannot, and that is its
 * one real limitation: it can only ever confirm a font that CANDIDATES already names, so
 * anything installed but unlisted stays invisible. Treat the list as best-effort coverage of
 * the fonts users of this app are likely to have, not as an inventory of the machine.
 *
 * A SECOND limitation matters for the te'amim picker: width measurement says whether a family
 * resolved, never what its cmap covers, so this file cannot report which fonts draw the
 * cantillation marks. fontsApi falls back to the bundled faces for that list rather than
 * guessing at the machine's.
 *
 * How the probe works: render the Hebrew alphabet at 72px in `'<font>', <base>` and compare the
 * measured width against the same text in `<base>` alone. If the width differs, the browser
 * resolved <font> rather than falling through to <base>, so the font is installed. Three
 * different base families are tried because a candidate can coincidentally match one of them.
 *
 * Kept in its own file deliberately: the caller stays a two-branch function, and the 99-name
 * list does not sit in the middle of the real enumeration path.
 */

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

/** Names from CANDIDATES that the browser can actually resolve. May be empty. */
export function detectFontsByCanvas(): string[] {
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
