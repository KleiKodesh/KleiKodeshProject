import restrictedBooks from './exportRestrictedBooks.json'

/**
 * Books that must not leave the app in an editable form (Word). Reading, printing and PDF
 * are untouched; only the editable export is refused.
 *
 * The list is Otzaria's own `assets/export_restricted_books.json`, copied verbatim so the
 * two apps enforce the same publisher agreements on the same seforim DB. Entries are exact
 * library titles, matched the way Otzaria matches them: trimmed, inner whitespace collapsed,
 * nothing else. So a listed volume is covered and an unlisted sibling volume is not - that is
 * the agreement's shape, not an oversight here.
 */
const RESTRICTED_TITLES = new Set(restrictedBooks.books.map(normalizeTitle))

function normalizeTitle(title: string): string {
  return title.trim().replace(/\s+/g, ' ')
}

export function isExportRestricted(title: string | undefined | null): boolean {
  return !!title && RESTRICTED_TITLES.has(normalizeTitle(title))
}
