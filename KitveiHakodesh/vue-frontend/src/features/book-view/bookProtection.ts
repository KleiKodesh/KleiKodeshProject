import { useBooksDataStore } from '@/stores/booksDataStore'
import { showToast } from '@/composables/useToast'
import { t } from '@/i18n'
import type { BookProtection } from '@/webview-host/queries.types'

/**
 * The publisher's limits on a book, as Core computed them (BookProtection.cs, from Otzaria's
 * `book_protection` table) and the catalog carries them. This file only reads and enforces
 * them; the rules themselves (which level allows what, the 40-line limits) live in Core.
 */

/** The book's limits, or null when it has none (or is not a library book). */
export function bookProtectionOf(bookId: number | null | undefined): BookProtection | null {
  if (bookId == null) return null
  return useBooksDataStore().allBooksMap.get(bookId)?.protection ?? null
}

/** For output that combines books: the strictest one (Core's Strictest - the highest level). */
export function strictestBookProtection(protections: Iterable<BookProtection | null | undefined>): BookProtection | null {
  let strictest: BookProtection | null = null
  for (const protection of protections)
    if (protection && protection.level > (strictest?.level ?? 0)) strictest = protection
  return strictest
}

/**
 * Whether a copy of `lineCount` lines may go ahead. Over the limit the copy is refused whole
 * and the user is told why - as Otzaria does (copy_guard.dart ensureCopyAllowed).
 */
export function ensureCopyAllowed(protection: BookProtection | null, lineCount: number): boolean {
  const max = protection?.maxCopyLines
  if (max == null || lineCount <= max) return true
  showToast(t('bookView.copyLimitedByPublisher', { max }), { variant: 'error', duration: 6000 })
  return false
}

/**
 * A copy that spans several books (a commentary pane): each protected book's selected lines
 * are counted separately, and the largest count is checked against the strictest limit -
 * Otzaria's buildCommentaryCopyGuard.
 */
export function ensureMultiBookCopyAllowed(selectedLines: Iterable<{ bookId: number; lineId: number }>): boolean {
  const linesByBook = new Map<number, Set<number>>()
  const protections: BookProtection[] = []
  for (const { bookId, lineId } of selectedLines) {
    let lines = linesByBook.get(bookId)
    if (!lines) {
      const protection = bookProtectionOf(bookId)
      if (!protection) continue
      protections.push(protection)
      linesByBook.set(bookId, (lines = new Set()))
    }
    lines.add(lineId)
  }
  let largest = 0
  for (const lines of linesByBook.values()) largest = Math.max(largest, lines.size)
  return ensureCopyAllowed(strictestBookProtection(protections), largest)
}
