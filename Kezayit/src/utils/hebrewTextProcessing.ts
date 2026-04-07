/**
 * Hebrew text processing utilities.
 * State 0: full diacritics (nikkud + cantillation)
 * State 1: remove cantillation only (U+0591–U+05AF)
 * State 2: remove nikkud as well (U+05B0–U+05BD, U+05C1, U+05C2, U+05C4, U+05C5)
 */
export function applyDiacriticsFilter(html: string, state: number): string {
  if (state === 0 || !html || html === '\u00A0') return html

  const div = document.createElement('div')
  div.innerHTML = html

  const walker = document.createTreeWalker(div, NodeFilter.SHOW_TEXT, null)
  const nodes: Text[] = []
  let node: Node | null
  while ((node = walker.nextNode())) nodes.push(node as Text)

  for (const textNode of nodes) {
    let t = textNode.nodeValue ?? ''
    if (state >= 1) t = t.replace(/[\u0591-\u05AF]/g, '')
    if (state >= 2) t = t.replace(/[\u05B0-\u05BD\u05C1\u05C2\u05C4\u05C5]/g, '')
    textNode.nodeValue = t
  }

  return div.innerHTML
}

/** Strip all Hebrew diacritics for search matching. */
export function removeDiacriticsForSearch(text: string): string {
  return text.replace(/[\u0591-\u05C7]/g, '')
}
