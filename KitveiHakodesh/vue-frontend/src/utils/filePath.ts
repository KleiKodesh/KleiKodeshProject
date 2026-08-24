/**
 * Filesystem path splitting.
 *
 * Paths reach the frontend from two places that disagree about separators: the
 * WebView2 host returns Windows backslashes, while the dev service can carry
 * forward slashes through. Every helper here accepts either, which is why they
 * all search for the last of both rather than picking one.
 */

/** Index of the last separator of either kind, or -1 when the path has none. */
function lastSeparator(path: string): number {
  return Math.max(path.lastIndexOf('\\'), path.lastIndexOf('/'))
}

/**
 * The directory containing the given file, without a trailing separator.
 *
 * Returns an empty string for a bare filename — a path with no separator has no
 * knowable parent, and guessing one (the CWD, say) would attribute a visit to a
 * folder the user never chose.
 */
export function parentFolder(filePath: string): string {
  const cut = lastSeparator(filePath)
  if (cut < 0) return ''
  // A root path ('C:\' or '/') keeps its separator: without it 'C:' reads as a
  // drive-relative path, which means something different on Windows.
  if (cut === 0) return filePath.slice(0, 1)
  if (filePath[cut - 1] === ':') return filePath.slice(0, cut + 1)
  return filePath.slice(0, cut)
}

/**
 * The name to show for a folder: its own last segment. A drive root yields its
 * drive ('C:\' gives 'C:'), and the whole string is the fallback for anything
 * that would otherwise render as an empty tile label.
 */
export function folderDisplayName(folderPath: string): string {
  if (!folderPath) return ''
  // Trailing separators would otherwise yield an empty final segment.
  const trimmed = folderPath.replace(/[\\/]+$/, '')
  if (!trimmed) return folderPath
  const cut = lastSeparator(trimmed)
  const name = cut < 0 ? trimmed : trimmed.slice(cut + 1)
  return name || folderPath
}
