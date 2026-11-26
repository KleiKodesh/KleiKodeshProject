/** Joins the segments of a card's path line. */
export const HOME_CARD_PATH_SEPARATOR = ' › '

/**
 * The full path a book card resumes to: catalog category path › book title › the
 * TOC breadcrumb of the saved position.
 *
 * `categoryPath` is the book's catalog categories, root first (stores/bookSummaries);
 * `tocPath` is the " · "-joined breadcrumb tabStore records on the recents location.
 * Either may be empty — the path then just has fewer segments.
 */
export function bookResumePath(
  categoryPath: readonly string[],
  title: string,
  tocPath: string | undefined,
): string {
  const tocSegments = tocPath ? tocPath.split(' · ').filter(Boolean) : []
  return [...categoryPath, title, ...tocSegments].join(HOME_CARD_PATH_SEPARATOR)
}
