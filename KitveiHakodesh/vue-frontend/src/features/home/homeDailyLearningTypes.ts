/** What to look up in the library for one daily-learning card. */
export interface HomeDailyLearningTarget {
  /** Book names tried in order against the catalog (exact titles when matchTitleExactly). */
  bookQueries: string[]
  matchTitleExactly?: boolean
  /** SQL LIKE patterns tried in order against the book's TOC entry text. */
  tocPatterns: string[]
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
  label: string
  location: string
  target: HomeDailyLearningTarget
}
