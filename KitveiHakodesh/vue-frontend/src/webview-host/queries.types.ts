/**
 * Row shapes returned by the seforim queries in `queries.sql.ts`.
 *
 * Every type used as a `query<T>` parameter or a `{ rows: T[] }` service reply belongs here,
 * beside the SQL that produces it — the same rule `queries.sql.ts` applies to the SQL strings
 * themselves. Several of these previously lived in feature folders, which forced `seforimApi.ts`
 * to import upward into `features/` just to name its own results, and made a UI feature the
 * authority for a contract the C# service also has to match (`SeforimModels.cs`).
 *
 * This file has **no imports**, deliberately. A row shape describes what SQLite returned; it must
 * not reference a component prop type, a Vue type, or anything from a feature. Types that *build
 * on* a row — `CategoryNode` adding `children`/`books`, or anything holding rendered state — are
 * view models and belong to the feature that builds them.
 *
 * Naming: these mirror the SELECT lists, not the tables. If a query changes its columns, change
 * the type here in the same edit.
 */

// ── Catalog ───────────────────────────────────────────────────────────────────

export interface BookRow {
  id: number
  categoryId: number
  title: string
  hasTeamim?: number | null // 1 if the book has cantillation marks, 0/null if not
  authors?: string | null
  /**
   * The book's precedence in the library, computed ONCE by the service
   * (SeforimDbBookTreeOrder.cs) and shared with the catalog and full-text indexes, so every
   * ranking here ties on the same order. Optional only for custom (negative-id) books, which
   * the server never sees; `assignFullPaths` numbers those after the last server value.
   */
  treeOrder?: number
  parentPath?: string // category path without the book title — used for display in search results
  period?: string // Chronological period: תנ"ך, ספרות חז"ל, גאונים, ראשונים, אחרונים, etc.
  rootCategory?: string // First-tier category title
}

export interface CategoryRow {
  id: number
  parentId: number | null
  title: string
  level: number
}

/** Per-book metadata plus the connection-kind flags the book-view toolbar switches on. */
export interface BookInfo {
  totalLines: number
  hasTeamim: number
  hasTargumConnection: number
  hasReferenceConnection: number
  hasSourceConnection: number
  hasCommentaryConnection: number
  hasOtherConnection: number
}

/**
 * Everything the library records ABOUT a book - credits, provenance, licence - for the
 * book view's info popover. Mirrors BookMetadataRow in SeforimDbModels.cs. Optional fields
 * are null when blank or when the open library's schema lacks the column; lists are empty.
 */
export interface BookMetadataRow {
  title: string
  heRef: string | null
  collectiveTitle: string | null
  shortDescription: string | null
  description: string | null
  categoryTitle: string | null
  sourceName: string | null
  authors: string[]
  publicationPlaces: string[]
  publicationDates: string[]
  topics: string[]
  /** Licence codes as the library spells them (CC-BY, Public Domain, ...). */
  licenses: string[]
  /** Where the edition text came from - a name or a URL, as recorded. */
  editionSources: string[]
}

// ── Table of contents ─────────────────────────────────────────────────────────

/**
 * One TOC entry, flat — `parentId` gives the tree.
 *
 * The first five fields are structurally identical to `TreeNodeItem` in
 * `components/treeTypes.ts`, which is `TreeView`'s input contract. That is deliberate and there is
 * no `extends` between them: TypeScript is structural, so this satisfies that prop with no import
 * in either direction. Declaring the relationship would make the data layer depend on a component.
 */
export interface TocEntry {
  id: number
  parentId: number | null
  level: number
  hasChildren: boolean | number
  text: string
  lineId: number | null
  lineIndex: number | null
}

export interface AltTocStructure {
  id: number
  key: string
  title: string | null
  heTitle: string | null
}

/** TOC titles across several books — the catalog-search TOC fallback (`GET_TOC_TITLES_FOR_BOOKS`). */
export interface TocRow {
  id: number
  parentId: number | null
  bookId: number
  text: string
  lineIndex: number | null
  hasChildren: number | boolean
}

// ── Lines ─────────────────────────────────────────────────────────────────────

export interface LineRow {
  id: number
  lineIndex: number
  content: string
}

/**
 * One alternate version of a book — an overlay of replacement text over the SAME
 * line ids, so the TOC, links and highlights stay valid when the reader swaps to it.
 */
export interface BookVersionRow {
  id: number
  /** The upstream key, usually English. Stable — this is what persistence stores. */
  versionTitle: string
  /** Display name. Often absent, in which case the UI falls back to versionTitle. */
  heVersionTitle: string | null
  versionSource: string | null
  versionNotes: string | null
  heVersionNotes: string | null
}

/**
 * A scanned edition of an Otzaria book on HebrewBooks. Ordered by the service: the best
 * edition first, then certain matches before uncertain ones.
 */
export interface HebrewBooksEdition {
  /** The HebrewBooks file id - what the download and the local-folder lookup key on. */
  id: number
  title: string
  author: string
  printingPlace: string
  printingYear: string
  pages: number | null
  isBest: boolean
  /** False when the book-to-edition match is only probable. */
  isCertain: boolean
}

/** Reverse source/targum lookup — which lines point AT the given ones. */
export interface ReverseLineRow {
  sourceBookId: number
  sourceLineId: number
  lineIndex: number
  content: string
}

// ── Links ─────────────────────────────────────────────────────────────────────

export interface CommentaryLinkRow {
  targetBookId: number
  targetLineId: number
  connectionTypeId: number
  lineIndex: number
  content?: string
}

/**
 * Forward commentary links grouped per (target book, connection type): DISTINCT
 * target line ids with their line indexes as parallel arrays. Ids and numbers only.
 */
export interface CommentaryLinkGroupRow {
  targetBookId: number
  connectionTypeId: number
  lineIds: number[]
  lineIndexes: number[]
}

/** Per-word link anchor (schema v2+ `link_anchor` table; absent in current v1 DBs). */
export interface WordLinkAnchor {
  lineId: number
  charStart: number
  charEnd: number | null
  label: string | null
  targetBookId: number
  targetLineId: number
  targetLineIndex: number
  sourceBookId: number
  connectionTypeId: number
  // Client-assigned by useWordLinkAnchors (NOT from the wire): per-book fallback
  // treatment slot and runtime-chosen enclosure glyphs. Absent → the splicer's
  // modulo fallback applies. See buildWordLinkTreatments in wordLinkAnchors.ts.
  colorBucket?: number
  encOpen?: string
  encClose?: string
}

/** One distinct (commentary book, anchor label) pair of a source book's word-link anchors. */
export interface WordLinkTargetRow {
  targetBookId: number
  label: string | null
  connectionTypeId: number
  orderIndex: number
}
