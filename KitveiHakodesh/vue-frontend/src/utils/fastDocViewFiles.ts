/* Which local files open in the FastDocView page. Every file-opening path (picker,
   search results, address bar, recents, push events) asks opensInFastDocView. */

/**
 * THE switch for FastDocView routing. Off until the service's fastDocView* ops exist:
 * Word-family files keep opening as PDF (Word -> PDF conversion) and .txt in /txt-view.
 * Turning it on routes them to /fast-doc-view (see features/fast-doc-view/README.md).
 */
export const OPEN_DOCUMENTS_IN_FAST_DOC_VIEW = false

/** Extensions FastDocView reads - Word family, OpenDocument text, RTF and plain text. */
const FAST_DOC_VIEW_EXTENSIONS = new Set([
  '.doc', '.docx', '.docm', '.dot', '.dotx', '.dotm', '.odt', '.ott', '.fodt', '.rtf', '.txt',
])

/** Whether a local file opens in FastDocView (always false while the switch is off). */
export function opensInFastDocView(filePath: string | undefined | null): boolean {
  if (!OPEN_DOCUMENTS_IN_FAST_DOC_VIEW || !filePath) return false
  return FAST_DOC_VIEW_EXTENSIONS.has(filePath.substring(filePath.lastIndexOf('.')).toLowerCase())
}
