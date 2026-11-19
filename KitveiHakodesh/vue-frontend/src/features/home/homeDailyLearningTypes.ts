/**
 * One way to name the book of a daily-learning card: its title, optionally only inside one
 * library section. Titles compare with spelling folded (see homeDailyLearningBookMatch.ts),
 * and inside a section the base text wins over its commentaries.
 */
export interface HomeDailyLearningBookName {
  title: string
  /** Leading category path the book must sit in, e.g. 'תלמוד בבלי' or 'הלכה / משנה תורה'. */
  section?: string
}

/**
 * One TOC level: SQL-LIKE patterns (only `%`) tried in order. In the `anySpelling` form the
 * heading words compare with spelling folded (for names like parshiyot; never for numbered
 * headings, where folding would make "פרק כו" equal "פרק כ").
 */
export type HomeDailyLearningTocStep = string[] | { anySpelling: string[] }

/** What to look up in the library for one daily-learning card. */
export interface HomeDailyLearningTarget {
  /** Book names tried in order; the first one in the library is opened. */
  books: HomeDailyLearningBookName[]
  /**
   * Where in the book: one step per TOC level, each looked for under the previous step's
   * entry. The card opens at the deepest step found (the book start when none is).
   */
  tocPath: HomeDailyLearningTocStep[]
  /** Alt-TOC structure key to look in when the main TOC has no match (e.g. 'Vilna'); any when absent. */
  altTocKey?: string
  /** Main-TOC path tried when neither TOC has `tocPath` (e.g. Tehillim without its day-cycle alt TOC → the first perek). */
  fallbackTocPath?: HomeDailyLearningTocStep[]
  /**
   * Where to look in the book's lines when neither TOC has the place (one small query on
   * that book only): a page marker in the text, or the end of a line's heRef.
   */
  lineAnchor?: HomeDailyLearningLineAnchor
}

/** A line found by its text instead of a TOC heading. */
export interface HomeDailyLearningLineAnchor {
  /** Page marker: overlay name (e.g. 'Vilna Pages') and accepted values, e.g. ['לד.', '34a']. */
  pageMarker?: { overlay: string; values: string[] }
  /** heRef ending, e.g. ', יב' for mitzvah 12 of a mitzvah list. */
  heRefSuffix?: string
}

export interface HomeDailyLearningItem {
  key:
    | 'daf-yomi'
    | 'amud-yomi'
    | 'yerushalmi-yomi'
    | 'mishnah-yomit'
    | 'nach-yomi'
    | 'daily-psalms'
    | 'chofetz-chaim'
    | 'shemirat-halashon'
    | 'weekly-parsha'
    | 'rambam-1'
    | 'rambam-3'
    | 'kitzur-shulchan-aruch'
    | 'perek-yomi'
    | 'pirkei-avot'
    | 'daf-weekly'
  label: string
  location: string
  target: HomeDailyLearningTarget
}
