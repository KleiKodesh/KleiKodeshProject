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

/**
 * Serializes a selection Range to HTML with its partially-selected ancestors closed.
 *
 * `range.cloneContents()` drops any ancestor element that OPENS outside the range, so
 * a selection begun mid-heading clones as the heading's tail with no <h2> around it —
 * and the reverse case, a selection that ends outside, clones an <h2> whose close tag
 * never arrives. Serializing that through innerHTML lets the browser "repair" it by
 * extending the heading over everything that follows, which is why a drag started in
 * the middle of a line pasted into Word with the whole payload styled as a heading.
 *
 * Fix: rebuild the ancestor chain between the range's start and `scopeEl`, cloning
 * each element shallowly (attributes kept, children dropped) and nesting the fragment
 * inside it. Every tag the fragment opens is then closed at the selection's end, so
 * <h1>-<h6> and any inline wrapper still export as themselves — just bounded to the
 * text the user actually selected.
 *
 * `scopeEl` bounds the walk so the chain stops at the scroller rather than climbing to
 * <body> and dragging layout containers into the payload; pass the copy scroller. When
 * it is null the walk is unbounded and stops at the document root.
 */
export function serializeRangeBalanced(range: Range, scopeEl: HTMLElement | null): string {
  const fragment = range.cloneContents()

  // Elements already inside the fragment are balanced by construction — only the
  // ancestors the clone left behind need rebuilding.
  const chain: HTMLElement[] = []
  let node: HTMLElement | null =
    range.startContainer.nodeType === Node.TEXT_NODE
      ? range.startContainer.parentElement
      : (range.startContainer as HTMLElement)
  while (node && node !== scopeEl) {
    chain.push(node)
    node = node.parentElement
  }

  // Innermost ancestor first, so wrapping outward reproduces the original nesting.
  let wrapped: Node = fragment
  for (const ancestor of chain) {
    const shell = ancestor.cloneNode(false) as HTMLElement
    shell.appendChild(wrapped)
    wrapped = shell
  }

  const tmp = document.createElement('div')
  tmp.appendChild(wrapped)
  return tmp.innerHTML
}
