/**
 * The wire shape of a "דיווח על טעות בספר" report, and the one function that builds it.
 *
 * The field names are snake_case and the schema is fixed: this is a contract with a
 * server, not an app-internal type. It mirrors Otzaria's error-report schema v2 field
 * for field, so one collector can receive reports from either app. Do not rename a
 * field here to match app conventions — the server would stop understanding it.
 */

import type { ErrorReportSource } from './errorReportSource'

export const ERROR_REPORT_SCHEMA_VERSION = 2

/**
 * What the user is claiming. 'freeText' = "something is wrong here, here's what";
 * 'textCorrection' = "and here is the text it should say" — the second carries a
 * `correction` block and is the only kind a server can apply mechanically.
 */
export type ErrorReportKind = 'freeText' | 'textCorrection'

/**
 * A proposed replacement text. EXACT by contract: none of these fields is ever
 * truncated by the caps below, because a correction that was shortened in transit
 * would be applied as a deletion of whatever got cut.
 */
export interface ErrorReportCorrection {
  /** The whole source line, as it reads now. */
  original_line: string
  /** The part of it the user selected (equals original_line when nothing was selected). */
  original_selection: string
  /** Where original_selection starts inside original_line; -1 when it could not be located. */
  selection_offset: number
  /** The replacement. null = no proposal at all; '' = the user proposes deleting the text. */
  proposed_text: string | null
  /** The untouched text on either side, so a server can re-anchor the selection. */
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
    line_index: number
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
  const body = JSON.stringify(payload)
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
 * A digest over what the report IDENTIFIES — never over the timestamp or the reporter.
 * Two people reporting the same mistake on the same line must produce the same digest,
 * which is exactly what the server's `duplicate` flag is decided on.
 */
function buildContentDigest(parts: {
  bookTitle: string
  currentRef: string
  lineNumber: number
  selectedText: string
  errorDetails: string
  proposedText: string | null
}): string {
  return stableHash(
    [
      parts.bookTitle,
      parts.currentRef,
      String(parts.lineNumber),
      parts.selectedText,
      parts.errorDetails,
      parts.proposedText ?? '',
    ].join('\u0000'),
  )
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
  const hasCorrection = draft.proposedText !== null && location !== null
  const payload: ErrorReportPayload = {
    report_id: buildReportId(bookTitle, currentRef, selectedText),
    sender_email: draft.senderEmail,
    subject,
    book_title: bookTitle,
    current_ref: currentRef,
    line_number: draft.lineNumber,
    selected_text: selectedText,
    error_details: draft.errorDetails,
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
    report_kind: hasCorrection ? 'textCorrection' : 'freeText',
    content_digest: buildContentDigest({
      bookTitle,
      currentRef,
      lineNumber: draft.lineNumber,
      selectedText,
      errorDetails: draft.errorDetails,
      // The proposal as SENT, not as typed: a dropped correction must not make two
      // otherwise identical reports digest differently.
      proposedText: hasCorrection ? draft.proposedText : null,
    }),
    // Null throughout when the line could not be vouched for — see resolveErrorReportSource.
    // A collector must be able to tell "this is line 42 of book 7 in library build 31"
    // from "someone reported something about a line we cannot locate".
    location: {
      // -1, not the unverified draft index: "line 42 of no book" is indistinguishable
      // from a verified index whose book id happened to be missing, and a collector
      // would re-anchor on it.
      line_index: location ? location.lineIndex : -1,
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
    payload.correction = {
      original_line: draft.originalLine,
      original_selection: draft.selectedText,
      selection_offset: draft.selectionOffset,
      proposed_text: draft.proposedText,
      context_before: context.before,
      context_after: context.after,
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
