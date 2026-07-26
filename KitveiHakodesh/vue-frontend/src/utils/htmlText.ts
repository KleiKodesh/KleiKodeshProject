/**
 * Small helpers for moving between plain text and HTML strings, used by the copy
 * paths where a value is assembled as plain text but the clipboard writer treats
 * the whole result as HTML (setData('text/html', …)). Without these, bare `<`/`>`/`&`
 * in the text would be interpreted as markup and lost from the text/plain round-trip.
 */

/**
 * Escapes plain text for safe embedding in an HTML string. Escape a value ONCE,
 * and only after any entities have been decoded (see htmlToText) — escaping an
 * already-encoded entity like `&thinsp;` would double-encode it to `&amp;thinsp;`.
 */
export function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

/**
 * Decodes an HTML-ish string (tags stripped, entities still encoded) to real plain
 * text — e.g. `&amp;` -> `&`, `&thinsp;` -> the thin space char. Run before
 * escapeHtml so surviving entities aren't double-encoded on the way to the clipboard.
 */
export function htmlToText(html: string): string {
  const tmp = document.createElement('div')
  tmp.innerHTML = html
  return tmp.textContent ?? ''
}
