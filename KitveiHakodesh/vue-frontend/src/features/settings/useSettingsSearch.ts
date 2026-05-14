import { ref, watch, nextTick, type Ref } from 'vue'

/**
 * DOM-walker based settings search.
 *
 * Each section must be wrapped in a `<div data-section="id" data-section-label="label">`.
 * On every query change the composable waits for the next render tick, then:
 *   1. Finds every `[data-section]` element that is a direct child of the scroll container
 *      (or a direct child of a direct child — to handle fragment-root components like
 *      SettingsAdvancedPane whose sections land as siblings in the scroll container).
 *   2. Reads all text nodes inside that element, stopping at nested `[data-section]`
 *      boundaries so sections never bleed into each other.
 *   3. Toggles `data-section-hidden` on sections whose text doesn't match.
 *
 * No keyword arrays. No manual maintenance. The search always reflects exactly
 * what is rendered in the DOM — including button labels, hints rendered as text,
 * option labels, and description paragraphs inside child components.
 */
export function useSettingsSearch(scrollContainerRef: Ref<HTMLElement | null>) {
  const searchQuery = ref('')

  // Collect all text node content from `root`, but do NOT descend into
  // nested [data-section] elements — each section is its own search unit —
  // and do NOT descend into [data-search-ignore] elements (prose descriptions
  // that would cause false cross-section matches).
  function collectText(root: Element): string {
    const parts: string[] = []

    function walk(node: Node) {
      if (node.nodeType === Node.TEXT_NODE) {
        const text = node.textContent?.trim()
        if (text) parts.push(text)
        return
      }
      if (node.nodeType === Node.ELEMENT_NODE) {
        const el = node as Element
        // Stop at nested section boundaries
        if (el !== root && el.hasAttribute('data-section')) return
        // Skip prose descriptions marked as search-ignore
        if (el.hasAttribute('data-search-ignore')) return
        for (const child of el.childNodes) walk(child)
      }
    }

    walk(root)
    return parts.join(' ')
  }

  // Find all [data-section] elements that live inside the scroll container,
  // regardless of whether they are direct children or inside a fragment-root
  // component (which renders its elements as direct siblings in the container).
  function findSections(container: HTMLElement): HTMLElement[] {
    return Array.from(container.querySelectorAll<HTMLElement>('[data-section]')).filter(
      // Only top-level sections — exclude any that are nested inside another [data-section]
      (el) => !el.parentElement?.closest('[data-section]'),
    )
  }

  function applyFilter(query: string) {
    const container = scrollContainerRef.value
    if (!container) return
    const sections = findSections(container)
    sections.forEach((section) => {
      if (!query) {
        section.removeAttribute('data-section-hidden')
        return
      }
      const text = collectText(section)
      if (text.includes(query)) {
        section.removeAttribute('data-section-hidden')
      } else {
        section.setAttribute('data-section-hidden', '')
      }
    })
  }

  watch(searchQuery, async (query) => {
    await nextTick()
    applyFilter(query.trim())
  })

  // Build the nav panel entry list from the live DOM so it always matches
  // what is actually rendered, in document order.
  function getSectionNavEntries(): { id: string; label: string }[] {
    const container = scrollContainerRef.value
    if (!container) return []
    return findSections(container).map((el) => ({
      id: el.dataset.section ?? '',
      label: el.dataset.sectionLabel ?? '',
    }))
  }

  return { searchQuery, getSectionNavEntries }
}
