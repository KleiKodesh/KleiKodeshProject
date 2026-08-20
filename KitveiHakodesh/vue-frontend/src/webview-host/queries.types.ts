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
}
