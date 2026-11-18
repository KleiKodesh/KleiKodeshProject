/**
 * Keeps a detached window looking like the app: copies the app's stylesheets and
 * the theme attributes of <html> / <body> into it, and keeps both up to date
 * (a theme switch, a font setting, styles added later by a lazily loaded page).
 *
 * The copies are clones. A stylesheet node cannot be in two documents at once.
 */

const STYLE_SELECTOR = 'style, link[rel="stylesheet"]'

/** Returns the function that stops the mirroring. */
export function mirrorPageStyles(source: Document, target: Document): () => void {
  const clones = new Map<Element, Element>()

  function syncStylesheets() {
    const current = new Set(source.head.querySelectorAll(STYLE_SELECTOR))
    for (const [original, clone] of clones) {
      if (current.has(original)) continue
      clone.remove()
      clones.delete(original)
    }
    for (const original of current) {
      const existing = clones.get(original)
      if (existing) {
        // An inline <style> edited in place (the dev server's hot reload does this).
        if (existing.textContent !== original.textContent) existing.textContent = original.textContent
        continue
      }
      const clone = original.cloneNode(true) as Element
      clones.set(original, clone)
      target.head.appendChild(clone)
    }
  }

  function syncAttributes(from: Element, to: Element) {
    for (const name of to.getAttributeNames()) if (!from.hasAttribute(name)) to.removeAttribute(name)
    for (const name of from.getAttributeNames()) {
      const value = from.getAttribute(name)!
      if (to.getAttribute(name) !== value) to.setAttribute(name, value)
    }
  }

  function syncRootAttributes() {
    syncAttributes(source.documentElement, target.documentElement)
    syncAttributes(source.body, target.body)
  }

  syncStylesheets()
  syncRootAttributes()

  const headObserver = new MutationObserver(syncStylesheets)
  headObserver.observe(source.head, { childList: true, subtree: true, characterData: true })
  const rootObserver = new MutationObserver(syncRootAttributes)
  rootObserver.observe(source.documentElement, { attributes: true })
  rootObserver.observe(source.body, { attributes: true })

  return () => {
    headObserver.disconnect()
    rootObserver.disconnect()
  }
}
