import { IconComment20Regular } from '@iconify-prerendered/vue-fluent'
import { IconCopy20Regular, IconDocumentArrowRight20Regular, IconSearch20Regular, IconLibrary20Regular, IconImageCopy20Regular } from '@iconify-prerendered/vue-fluent'
import { t } from '@/i18n'
import { shallowRef, onScopeDispose, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import type ContextMenu from '@/components/ContextMenu.vue'
import type { ContextMenuItem } from '@/components/ContextMenu.vue'
import { attachScopedCopy, triggerCopy } from '@/composables/useLineCopy'
import { useContextMenuLongPress } from '@/composables/useContextMenuLongPress'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { pasteIntoWord } from '@/webview-host/bridge'
import { showToast } from '@/composables/useToast'
import { toHebrewSearchQuery } from '@/features/book-view/lines/useBookViewLineCopyMenu'
import { selectionToHtml, selectionToText } from './pdfSelectionText'

/**
 * The PDF view's right-click / long-press menu (the old viewer's items): copy, copy into Word,
 * search the selection in the library / catalog, add a note on it, copy the page as an image.
 * Copies (menu and Ctrl+C) go out as RTL Word-friendly text with the word gaps of OCR'd pages.
 */
export function usePdfReaderContextMenu(
  scroller: Ref<HTMLElement | null>,
  menuRef: Ref<InstanceType<typeof ContextMenu> | null>,
  options: {
    isBlocked: () => boolean
    copyPageAsImage: () => void
    searchInLibrary: (query: string) => void
    searchInCatalog: (query: string) => void
    /** "Add note" for the selection as it is now: a note at its end (usePdfNotes), no highlight; null = none. */
    noteAtSelection: () => (() => void) | null
  },
) {
  const hostWindow = useHostWindow()
  const items = shallowRef<ContextMenuItem[]>([])
  // The selection's text when the menu opened; the menu's own copy falls back to it.
  let capturedText = ''
  let menuCopyInProgress = false

  /** The selected text when the selection lies in this viewer's pages, else ''. */
  function selectedText(): string {
    const sel = hostWindow.getSelection()
    const el = scroller.value
    if (!el || !sel || sel.rangeCount === 0 || sel.isCollapsed) return ''
    if (!el.contains(sel.getRangeAt(0).commonAncestorContainer)) return ''
    return selectionToText(hostWindow)
  }

  // Every copy of text selected in the pages (Ctrl+C and the menu) gets the Word-friendly shape.
  // Capture phase on the document: PDF.js's text layer stops the copy event at the target.
  onScopeDispose(attachScopedCopy(hostWindow.document, () => {
    const text = selectedText() || (menuCopyInProgress ? capturedText : '')
    return selectionToHtml(text) || null
  }))

  function copySelection(afterCopy?: () => void): boolean {
    menuCopyInProgress = true
    try {
      return triggerCopy(afterCopy, hostWindow.document)
    } finally {
      menuCopyInProgress = false
    }
  }

  function copyIntoWord(text = selectedText()) {
    if (!text.trim()) return
    capturedText = text
    // Word is told to paste only once the clipboard really holds the text.
    if (!copySelection(() => void pasteIntoWord().catch(() => {}))) showToast(t('pdfReader.menu.copyFailed'))
  }

  function searchSelection(text = selectedText(), inCatalog = false) {
    const query = toHebrewSearchQuery(text)
    if (!query) return
    if (inCatalog) options.searchInCatalog(query)
    else options.searchInLibrary(query)
  }

  // The book view's order: the copy actions, then add note.
  function buildItems(text: string): ContextMenuItem[] {
    const hasText = !!text.trim()
    const list: ContextMenuItem[] = []
    if (hasText) {
      list.push({ label: t('pdfReader.menu.copy'), icon: IconCopy20Regular, action: () => void copySelection(), shortcut: 'Ctrl+C' })
      list.push({ label: t('pdfReader.menu.copyIntoWord'), icon: IconDocumentArrowRight20Regular, action: () => copyIntoWord(text), shortcut: 'Ctrl+V' })
      list.push({ label: t('pdfReader.menu.searchInLibrary'), icon: IconSearch20Regular, action: () => searchSelection(text), shortcut: 'Ctrl+Shift+C' })
      list.push({ label: t('pdfReader.menu.searchInCatalog'), icon: IconLibrary20Regular, action: () => searchSelection(text, true) })
    }
    list.push({ label: t('pdfReader.menu.copyPageAsImage'), icon: IconImageCopy20Regular, action: options.copyPageAsImage })
    // Read where the selection ends now: clicking the menu may clear it.
    const addNote = hasText ? options.noteAtSelection() : null
    if (addNote) list.push({ label: t('pdfReader.notes.add'), icon: IconComment20Regular, action: addNote })
    return list
  }

  function openAt(x: number, y: number) {
    capturedText = selectedText()
    items.value = buildItems(capturedText)
    void menuRef.value?.showAtPosition(x, y)
  }

  useEventListener(scroller, 'contextmenu', (e: MouseEvent) => {
    if (options.isBlocked()) return
    // Only over the pages: form fields and the editors keep the browser's own menu.
    if ((e.target as Element | null)?.closest('input, textarea, select, [contenteditable="true"]')) return
    e.preventDefault()
    openAt(e.clientX, e.clientY)
  })
  useContextMenuLongPress(scroller, (e) => {
    if (!options.isBlocked()) openAt(e.clientX, e.clientY)
  })

  /** Builds the items for the selection as it is now (the selection toolbar, before it shows). */
  function prepareForSelection() {
    capturedText = selectedText()
    items.value = buildItems(capturedText)
  }

  return { items, selectedText, copyIntoWord, searchSelection, prepareForSelection }
}
