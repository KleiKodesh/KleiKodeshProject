/**
 * The wire shape of a "דיווח על טעות בספר" report, and the one function that builds it.
 *
 * The field names are snake_case and the schema is fixed: this is a contract with a
 * server, not an app-internal type. It mirrors Otzaria's error-report schema v2 field
 * for field, so one collector can receive reports from either app. Do not rename a
 * field here to match app conventions — the server would stop understanding it.
 */

import type { ErrorReportSource } from './errorReportSource'
import { canonicalJsonSha256 } from './canonicalJson'

export const ERROR_REPORT_SCHEMA_VERSION = 2

/**
 * What the user is claiming. 'free_text' = "something is wrong here, here's what";
 * 'text_correction' = "and here is the text it should say" — the second carries a
 * `correction` block and is the only kind a server can apply mechanically.
 */
export type ErrorReportKind = 'free_text' | 'text_correction'

/**
 * A proposed replacement text. EXACT by contract: none of these fields is ever
 * truncated by the caps below, because a correction that was shortened in transit
 * would be applied as a deletion of whatever got cut.
 */
export interface ErrorReportCorrection {
  /** The whole source line, as it reads now. */
  original_line: string
  /**
   * The part of original_line the user selected, or null for a whole-line correction.
   * null and `selection_offset` go together: the collector refuses a selection without
   * an offset, and an offset without a selection.
   */
  original_selection: string | null
  /**
   * Where the selection sits inside original_line, as a half-open range. null when the
   * selection could not be located — a correction of the whole line. The unit matters:
   * both ends are UTF-16 code units, which is what JS string indices already are.
   */
  selection_offset: { unit: 'utf16_code_units'; start: number; end: number } | null
  /** The replacement. null = no proposal at all; '' = the user proposes deleting the text. */
  proposed_text: string | null
  /**
   * The ENTIRE remainder of original_line on each side of the selection — not an
   * excerpt. The collector reconstructs the line and rejects the correction unless
   * `context_before + original_selection + context_after === original_line`, so a
   * few-words-of-context version fails validation on any longer line.
   */
  context_before: string
  context_after: string
}

export interface ErrorReportPayload {
  report_id: string
  sender_email: string
  subject: string
  book_title: string
  current_ref: string
  line_number: number
  selected_text: string
  error_details: string
  context_text: string
  file_path: string
  source_folder: string
  library_version: string
  /** ISO8601, UTC. */
  created_at: string
  schema_version: number
  report_kind: ErrorReportKind
  content_digest: string
  location: {
    line_index: number | null
    book_id: number | null
    library_build_id: string | null
    he_ref: string | null
  }
  source_hint: {
    source_folder: string
    library_relative_path: string
    repo_path: null
  }
  client: {
    app_version: string
    platform: string
  }
  correction?: ErrorReportCorrection
  /**
   * Queue-local, never sent: JSON.stringify drops it only because the server ignores
   * unknown keys, so it is stripped explicitly before the body is encoded. Marks a
   * report the collector refused under its previous id — kept so the user can resubmit
   * deliberately, skipped by every automatic flush.
   */
  manualResendOnly?: boolean
}

// ── Client-side caps ──────────────────────────────────────────────────────────
//
// Applied BEFORE the digest is computed, so the digest always describes exactly what
// the server will receive. A report that still exceeds the body cap after per-field
// truncation is rejected rather than silently mangled — see assertWithinBodyCap.

export const ERROR_REPORT_LIMITS = {
  maxSubjectLength: 500,
  /** Shared by book_title and current_ref — both are one-line references. */
  maxTitleOrRefLength: 300,
  maxSelectedTextLength: 10_000,
  maxContextTextLength: 20_000,
  /** The whole UTF-8 encoded body, in bytes. */
  maxApiBodyBytes: 256 * 1024,
  /**
   * The collector's own cap on correction.original_line, past which it answers 413.
   * A correction cannot be truncated to fit — the line IS the anchor — so an
   * over-long line sends as free text instead of being refused outright.
   */
  maxOriginalLineLength: 20_000,
  /** Keeps a pasted essay from making the whole body undeliverable. */
  maxErrorDetailsLength: 20_000,
} as const

/**
 * Cuts `text` to `maxLength` characters and marks the cut with an ellipsis.
 *
 * Never splits a surrogate pair: JS string indices are UTF-16 code units, so slicing
 * at an arbitrary index can land between the two halves of an astral character (and
 * Hebrew text carries plenty of them via musical/pointing marks in some sources),
 * leaving a lone surrogate that no UTF-8 encoder can represent.
 */
export function truncateText(text: string, maxLength: number): string {
  if (text.length <= maxLength) return text
  let cut = maxLength - 1
  // A high surrogate at the cut point means its low half is the next unit — drop both.
  const code = text.charCodeAt(cut - 1)
  if (code >= 0xd800 && code <= 0xdbff) cut -= 1
  return text.slice(0, cut) + '…'
}

function utf8ByteLength(text: string): number {
  return new TextEncoder().encode(text).length
}

/**
 * The encoded body, or null when even the capped report is too large to send.
 *
 * Returning null rather than trimming further is deliberate: everything still big at
 * this point is correction text, and that is the one thing that must stay exact.
 */
export function encodeErrorReportBody(payload: ErrorReportPayload): string | null {
  // manualResendOnly is our own bookkeeping and has no place on the wire.
  const { manualResendOnly: _omit, ...wire } = payload
  const body = JSON.stringify(wire)
  return utf8ByteLength(body) > ERROR_REPORT_LIMITS.maxApiBodyBytes ? null : body
}

// ── Identity ──────────────────────────────────────────────────────────────────

/**
 * FNV-1a, 32-bit, hex. Not a security hash and not meant to be one — it exists so two
 * reports of the same mistake land on the same `content_digest` and the server can
 * recognise the duplicate. Spelled out here rather than pulled from SubtleCrypto so
 * that building a payload stays synchronous; the dialog builds one on every keystroke.
 */
export function stableHash(text: string): string {
  let hash = 0x811c9dc5
  for (let i = 0; i < text.length; i++) {
    hash ^= text.charCodeAt(i)
    hash = Math.imul(hash, 0x01000193) >>> 0
  }
  return hash.toString(16).padStart(8, '0')
}

/**
 * `<microseconds since epoch>-<hash of what the report is about>`, matching Otzaria.
 *
 * The timestamp half makes it unique per attempt; the hash half makes two attempts at
 * the SAME mistake visibly related in a server log. Microseconds are synthesised from
 * milliseconds (JS has no microsecond clock) — the precision is cosmetic, uniqueness
 * comes from the random tail.
 */
export function buildReportId(bookTitle: string, currentRef: string, selectedText: string): string {
  const micros = Date.now() * 1000 + Math.floor(Math.random() * 1000)
  return `${micros}-${stableHash(`${bookTitle}\u0000${currentRef}\u0000${selectedText}`)}`
}

/**
 * The digest the collector decides its `duplicate` flag on.
 *
 * Byte-compatible with Otzaria by construction: SHA-256 over the OCJ-1 canonical
 * encoding of the SAME field map, including the `v: 1` marker. Every part of that
 * matters — a different hash, a different field set, or a different key order all
 * produce a digest that simply never collides with anyone else's, which looks like
 * nothing at all locally and means de-duplication silently does not work.
 *
 * Async because SubtleCrypto is. That is why it is computed once at send time rather
 * than inside buildErrorReportPayload, which the dialog calls on every keystroke.
 */
export function buildContentDigest(payload: ErrorReportPayload): Promise<string> {
  return canonicalJsonSha256({
    v: 1,
    report_kind: payload.report_kind,
    book_title: payload.book_title,
    current_ref: payload.current_ref,
    // The 0-based machine index, NOT the 1-based line_number a human reads.
    line_index: payload.location.line_index,
    selected_text: payload.selected_text,
    error_details: payload.error_details,
    context_text: payload.context_text,
    source_folder: payload.source_folder,
    file_path: payload.file_path,
    library_version: payload.library_version,
    correction: payload.correction
      ? {
          original_line: payload.correction.original_line,
          original_selection: payload.correction.original_selection,
          proposed_text: payload.correction.proposed_text,
          selection_offset: payload.correction.selection_offset,
        }
      : null,
  })
}

/** Stamps the digest onto a payload. The last step before the body is encoded. */
export async function withContentDigest(payload: ErrorReportPayload): Promise<ErrorReportPayload> {
  return { ...payload, content_digest: await buildContentDigest(payload) }
}

// ── Context extraction ────────────────────────────────────────────────────────

/** How many words of untouched text travel on each side of the selection. */
const CONTEXT_WORD_COUNT = 4

/**
 * The four words before and the four words after the selection inside its line —
 * Otzaria's rule, kept identical so both apps' reports read the same.
 *
 * Returns the two sides separately: the dialog shows them, and the correction block
 * sends them as the anchors a server re-locates the selection by.
 */
export function extractContextAround(
  line: string,
  selectionOffset: number,
  selectionLength: number,
): { before: string; after: string } {
  if (selectionOffset < 0) return { before: '', after: '' }
  const beforeWords = line.slice(0, selectionOffset).split(/\s+/).filter(Boolean)
  const afterWords = line
    .slice(selectionOffset + selectionLength)
    .split(/\s+/)
    .filter(Boolean)
  return {
    before: beforeWords.slice(-CONTEXT_WORD_COUNT).join(' '),
    after: afterWords.slice(0, CONTEXT_WORD_COUNT).join(' '),
  }
}

// ── Build ─────────────────────────────────────────────────────────────────────

/** Everything the dialog knows; `buildErrorReportPayload` turns it into the wire shape. */
export interface ErrorReportDraft {
  senderEmail: string
  bookTitle: string
  /** The human-readable reference — book + TOC path, as the copy menu's מקור builds it. */
  currentRef: string
  /** 1-based, for a human to read. 0 when no line could be identified. */
  lineNumber: number
  /** 0-based index into the book's lines; the machine-readable twin of lineNumber. */
  lineIndex: number
  bookId: number | null
  selectedText: string
  errorDetails: string
  /** The whole line the selection sits in, plain text. */
  originalLine: string
  /** Where selectedText starts inside originalLine; -1 when it could not be located. */
  selectionOffset: number
  /** The user's replacement, or null when they proposed none. '' means "delete this". */
  proposedText: string | null
  /** Where the line came from, and whether its coordinates could be verified. */
  source: ErrorReportSource
}

/**
 * The client identification. Deliberately coarse: this is a mistake report, not
 * telemetry, so it says which build to reproduce on and nothing about the machine.
 */
// TODO: wire the real build version through a vite `define`. The installer records
// v9.x in the registry, but nothing surfaces it to the frontend, and package.json is
// 0.0.0. Until then every report is unattributable to a build, which is a triage cost
// for whoever receives them.
export const ERROR_REPORT_APP_VERSION = '0.0.0'
const ERROR_REPORT_PLATFORM = 'windows-webview2'

export function buildErrorReportPayload(draft: ErrorReportDraft): ErrorReportPayload {
  const { maxSubjectLength, maxTitleOrRefLength, maxSelectedTextLength, maxContextTextLength } =
    ERROR_REPORT_LIMITS

  const bookTitle = truncateText(draft.bookTitle, maxTitleOrRefLength)
  const currentRef = truncateText(draft.currentRef, maxTitleOrRefLength)
  const selectedText = truncateText(draft.selectedText, maxSelectedTextLength)
  const subject = truncateText(`דיווח על טעות: ${currentRef}`, maxSubjectLength)

  const context = extractContextAround(
    draft.originalLine,
    draft.selectionOffset,
    draft.selectedText.length,
  )
  const contextText = truncateText(
    [context.before, context.after].filter(Boolean).join(' … '),
    maxContextTextLength,
  )

  // A correction says "replace THIS text, HERE". Without a verified location there is
  // no trustworthy "here", so the proposal is dropped and the report degrades to
  // freeText — the user's words still travel, they just cannot be applied mechanically.
  // Otzaria degrades the same way for the same reason.
  const location = draft.source.location

  // The collector refuses a proposal identical to what it would replace
  // ('proposal_identical'), and WHICH text that is depends on whether the selection
  // could be located: the selection when it could, the whole line when it could not.
  // Computed here so an unlocatable selection cannot slip through as a no-op change.
  const locatedSelection =
    draft.selectionOffset >= 0 &&
    draft.originalLine.slice(
      draft.selectionOffset,
      draft.selectionOffset + draft.selectedText.length,
    ) === draft.selectedText
  const replacedText = locatedSelection ? draft.selectedText : draft.originalLine
  // A correction block the collector would refuse outright is worse than no correction
  // block: the report dies with a 4xx instead of arriving as a description. Both of
  // these are its hard requirements on original_line — non-empty, and within its cap —
  // and neither can be fixed by trimming, because that line is the anchor itself.
  const canAnchorCorrection =
    draft.originalLine.length > 0 &&
    draft.originalLine.length <= ERROR_REPORT_LIMITS.maxOriginalLineLength
  const hasCorrection =
    draft.proposedText !== null &&
    location !== null &&
    draft.proposedText !== replacedText &&
    canAnchorCorrection
  const payload: ErrorReportPayload = {
    report_id: buildReportId(bookTitle, currentRef, selectedText),
    sender_email: draft.senderEmail,
    subject,
    book_title: bookTitle,
    current_ref: currentRef,
    line_number: draft.lineNumber,
    selected_text: selectedText,
    error_details: truncateText(draft.errorDetails, ERROR_REPORT_LIMITS.maxErrorDetailsLength),
    context_text: contextText,
    // Otzaria's file_path is a `book.filePath` column dressed up to look like a path;
    // neither seforim database in use here HAS that column, so there is nothing
    // truthful to put in it. It stays in the schema (the shape is shared with the
    // collector) and travels empty — unlike the two below, which are real.
    file_path: '',
    source_folder: draft.source.sourceFolder,
    library_version: draft.source.libraryVersion,
    created_at: new Date().toISOString(),
    schema_version: ERROR_REPORT_SCHEMA_VERSION,
    report_kind: hasCorrection ? 'text_correction' : 'free_text',
    // Stamped by withContentDigest at send time — it needs SubtleCrypto, which is async.
    content_digest: '',
    // Null throughout when the line could not be vouched for — see resolveErrorReportSource.
    // A collector must be able to tell "this is line 42 of book 7 in library build 31"
    // from "someone reported something about a line we cannot locate".
    location: {
      // null, never a sentinel: a collector testing `line_index != null` would read -1
      // as a real verified index and try to anchor there. book_id and he_ref beside it
      // go null the same way.
      line_index: location ? location.lineIndex : null,
      book_id: location?.bookId ?? null,
      library_build_id: draft.source.libraryBuildId,
      he_ref: location?.heRef ?? null,
    },
    source_hint: {
      source_folder: draft.source.sourceFolder,
      library_relative_path: '',
      repo_path: null,
    },
    client: {
      app_version: ERROR_REPORT_APP_VERSION,
      platform: ERROR_REPORT_PLATFORM,
    },
  }

  if (hasCorrection) {
    // The collector pairs these three strictly: a non-null original_selection REQUIRES
    // a selection_offset, and an offset without a selection is refused. So a selection
    // we could not locate inside the line is sent as a whole-line correction (both
    // null) rather than as a located one with a missing offset.
    const hasLocatedSelection = locatedSelection
    payload.correction = {
      original_line: draft.originalLine,
      original_selection: hasLocatedSelection ? draft.selectedText : null,
      selection_offset: hasLocatedSelection
        ? {
            unit: 'utf16_code_units',
            start: draft.selectionOffset,
            end: draft.selectionOffset + draft.selectedText.length,
          }
        : null,
      proposed_text: draft.proposedText,
      // Exact remainders, so the collector's reconstruction check passes. The
      // four-word `context_text` above is a separate, human-readable field.
      context_before: hasLocatedSelection ? draft.originalLine.slice(0, draft.selectionOffset) : '',
      context_after: hasLocatedSelection
        ? draft.originalLine.slice(draft.selectionOffset + draft.selectedText.length)
        : '',
    }
  }

  return payload
}

/** A fresh `report_id` on the same report — what a 409 id-conflict is answered with. */
export function reissueReportId(payload: ErrorReportPayload): ErrorReportPayload {
  return {
    ...payload,
    report_id: buildReportId(payload.book_title, payload.current_ref, payload.selected_text),
  }
}
