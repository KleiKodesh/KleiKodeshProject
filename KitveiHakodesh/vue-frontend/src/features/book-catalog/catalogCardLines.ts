import type { FsItem } from './useBookCatalog'

/**
 * Text lines of one catalog card, kept apart on purpose.
 *
 * The library an item came from used to be joined onto the secondary line
 * ("12 ספרים · otzarya-unofficial-books-plain"). That text is long, unbreakable
 * (hyphens, Latin) and mixes two directions, so in a narrow pane it wrapped to a
 * different number of lines depending on whether the vertical scrollbar was
 * showing. The grid sized its rows from the first pass and never re-measured
 * them, so the wrapped cards spilled over the rows below and their text drew on
 * top of each other. The fix is structural: every line is a fixed, single line
 * (see HomeSuggestionCard's `singleLine`), and the library is its own badge line
 * rather than a suffix of another line.
 */
export interface CatalogCardLines {
  /** Muted second line: book count for a folder, authors for a book. May be empty. */
  path: string
  /** Source library of an item from an additional library; empty for the main one. */
  badge: string
}

const DB_FILE_SUFFIX = /\.(?:db|sqlite3?|sqlite)$/i

/**
 * Label as shown in a badge. The raw label is a library name or a file name; a
 * trailing database extension carries no meaning for a reader, so it is dropped.
 * Never returns a longer string than it was given and never invents text: the
 * label keeps identifying the same library, which is what lets two same-named
 * folders from different libraries be told apart.
 */
export function libraryBadgeText(label: string | null | undefined): string {
  const trimmed = (label ?? '').trim()
  const withoutSuffix = trimmed.replace(DB_FILE_SUFFIX, '')
  return withoutSuffix || trimmed
}

/** Tooltip form of a library label: states what the name is. */
export function libraryTooltipText(label: string | null | undefined): string {
  const text = libraryBadgeText(label)
  return text ? `ספרייה נוספת: ${text}` : ''
}

/** Library an item came from, as the badge would read it; empty for the main library. Ignores the show/hide preference. */
export function getLibraryName(item: FsItem): string {
  return libraryBadgeText(item.kind === 'book' ? item.book.libraryLabel : item.node.libraryLabel)
}

/**
 * `showBadge` is the user's "show library label" preference. Hiding only empties
 * the visible badge: the library stays available through getLibraryName for the
 * tooltip, so the origin of an item is never lost.
 */
export function getCatalogCardLines(item: FsItem, showBadge = true): CatalogCardLines {
  const badge = showBadge ? getLibraryName(item) : ''
  if (item.kind === 'book') {
    return { path: (item.book.authors ?? '').trim(), badge }
  }
  const count = item.node.subtreeBookIds?.length ?? 0
  return { path: count ? `${count} ספרים` : '', badge }
}

/** True when any item shows a badge (every card then reserves the badge line). Never true while badges are hidden. */
export function hasLibraryBadges(items: readonly FsItem[], showBadge = true): boolean {
  return showBadge && items.some((item) => getLibraryName(item) !== '')
}
