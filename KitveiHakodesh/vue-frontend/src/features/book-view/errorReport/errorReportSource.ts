/**
 * Where a reported line actually came from, read out of the seforim database.
 *
 * Two independent halves, and keeping them independent is the point:
 *
 *   PROVENANCE  (source folder, library build) — which copy of which corpus the reader
 *               has open. Always resolvable, always sent.
 *   LOCATION    (book id, line index, heRef) — the authoritative coordinates of the
 *               line. Sent only when we can prove the database still says what the
 *               reader saw; otherwise null, and a proposed correction is dropped.
 *
 * Otzaria does exactly this split, and a report there can carry a source folder while
 * its location is entirely null. Coupling the two would mean one unverifiable line
 * costs the collector the ability to tell which library the report came from.
 */

import { getBookSourceName, getLibraryDbVersion, getLineRefAndContent } from '@/webview-host/seforimApi'
import { htmlToText } from '@/utils/htmlText'

/** What Otzaria sends when the library build cannot be identified. */
export const UNKNOWN_LIBRARY_VERSION = 'unknown'

/** Sources whose names carry this prefix are the reader's own books, not the corpus. */
const PERSONAL_SOURCE_PREFIX = 'Personal::'

export interface ErrorReportSource {
  /** `source.name`, e.g. 'Sefaria' — '' when it could not be resolved. */
  sourceFolder: string
  /** `schema_meta.db_version`, or 'unknown'. */
  libraryVersion: string
  /** The same value, nulled when unknown — what location.library_build_id carries. */
  libraryBuildId: string | null
  /**
   * The verified coordinates, or null when the line could not be vouched for. null
   * also forbids a correction: see resolveErrorReportSource.
   */
  location: { bookId: number; lineIndex: number; heRef: string | null } | null
}

/**
 * Is the database line still the line the reader is looking at?
 *
 * Compared as plain text with whitespace collapsed, because the rendered line carries
 * markup, note markers and citation anchors the stored text has no idea about. Not
 * compared any looser than that: the whole purpose is to stop a correction being
 * anchored to a line the library has since changed, and a fuzzy match would wave
 * through precisely the case this exists to catch.
 */
function isSameLine(storedHtml: string, displayedPlainText: string): boolean {
  const normalize = (text: string): string => htmlToText(text).replace(/\s+/g, ' ').trim()
  const stored = normalize(storedHtml)
  return stored !== '' && stored === displayedPlainText.replace(/\s+/g, ' ').trim()
}

export interface ResolveSourceInput {
  bookId: number | null
  lineIndex: number
  /** The line as the reader sees it, already reduced to plain text. */
  displayedLine: string
  /**
   * The alternate edition on screen, or null for the book's own text. A version is an
   * overlay: the line id is the same but the WORDS are not the ones stored on `line`,
   * so the verification read below would be comparing against the wrong text. Otzaria
   * refuses a location for an alternate edition for this reason, and so do we.
   */
  activeVersionId: number | null
}

/**
 * Resolves provenance always, and location only when it can be proven.
 *
 * Never throws: a report that cannot describe its origin is still worth sending, and a
 * database hiccup must not cost the user the text they just typed.
 */
export async function resolveErrorReportSource(input: ResolveSourceInput): Promise<ErrorReportSource> {
  const libraryVersion = (await getLibraryDbVersion().catch(() => undefined)) ?? UNKNOWN_LIBRARY_VERSION
  const rawSourceName = input.bookId == null
    ? undefined
    : await getBookSourceName(input.bookId).catch(() => undefined)
  const sourceFolder = (rawSourceName ?? '').startsWith(PERSONAL_SOURCE_PREFIX)
    ? rawSourceName!.slice(PERSONAL_SOURCE_PREFIX.length)
    : (rawSourceName ?? '')

  const base: ErrorReportSource = {
    sourceFolder,
    libraryVersion,
    libraryBuildId: libraryVersion === UNKNOWN_LIBRARY_VERSION ? null : libraryVersion,
    location: null,
  }

  // Each of these means "the text on screen is not the text in `line`", so there is
  // nothing authoritative to point a collector at.
  if (input.bookId == null || input.lineIndex < 0) return base
  if (input.activeVersionId != null && input.activeVersionId > 0) return base

  const stored = await getLineRefAndContent(input.bookId, input.lineIndex).catch(() => undefined)
  if (!stored || !isSameLine(stored.content ?? '', input.displayedLine)) return base

  return {
    ...base,
    location: { bookId: input.bookId, lineIndex: input.lineIndex, heRef: stored.heRef ?? null },
  }
}
