import type { BookRow } from '@/webview-host/queries.types'

/** Joins the segments of a card's path line. */
export const HOME_CARD_PATH_SEPARATOR = ' › '

/**
 * The full path a book card resumes to: catalog category path › book title › the
 * TOC breadcrumb of the saved position.
 *
 * `book.parentPath` is the catalog path booksDataStore assigns (" / "-joined, see
 * bookCatalogTree.assignFullPaths); `tocPath` is the " · "-joined breadcrumb
 * tabStore records on the recents location. Either may be missing — the path then
 * just has fewer segments.
 */
export function bookResumePath(
  book: BookRow | undefined,
  fallbackTitle: string,
  tocPath: string | undefined,
): string {
  const categories = book?.parentPath ? book.parentPath.split(' / ').filter(Boolean) : []
  const tocSegments = tocPath ? tocPath.split(' · ').filter(Boolean) : []
  return [...categories, book?.title ?? fallbackTitle, ...tocSegments].join(HOME_CARD_PATH_SEPARATOR)
}
