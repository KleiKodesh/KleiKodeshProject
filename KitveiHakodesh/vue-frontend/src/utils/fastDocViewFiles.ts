/* Which local files open in the FastDocView page. Every file-opening path (picker,
   search results, address bar, recents, push events) asks opensInFastDocView. */

/** Extensions FastDocView reads - Word family, OpenDocument text, RTF and plain text. */
const FAST_DOC_VIEW_EXTENSIONS = new Set([
  '.doc', '.docx', '.docm', '.dot', '.dotx', '.dotm', '.odt', '.ott', '.fodt', '.rtf', '.txt',
])

/**
 * Whether a local file opens in FastDocView: the Word family, OpenDocument text, RTF and .txt
 * (see features/fast-doc-view/README.md). "Open as PDF" on that page is the explicit Word -> PDF choice.
 */
export function opensInFastDocView(filePath: string | undefined | null): boolean {
  if (!filePath) return false
  return FAST_DOC_VIEW_EXTENSIONS.has(filePath.substring(filePath.lastIndexOf('.')).toLowerCase())
}
