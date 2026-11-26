import { IconCopy20Regular, IconDocumentArrowRight20Regular, IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { t } from '@/i18n'
import type { ContextMenuItem } from '@/components/ContextMenu.vue'
import { copyCleanMenuItems } from '@/components/copyCleanMenuItems'
import type { PaneNavigation } from '@/composables/usePaneNavigation'
import { pasteIntoWord } from '@/webview-host/bridge'
import { toHebrewSearchQuery } from '@/features/book-view/lines/useBookViewLineCopyMenu'

/**
 * The book view's text context menu (useBookViewLineCopyMenu) for a Word document: the same
 * labels, shortcuts and order, keeping only the items that need no book data. Copy is the
 * native copy of the rendered selection; the app-wide copy listener (GlobalContextMenu) applies
 * the shared "copy clean text" setting to it. Line links, bookmarks, notes, highlights, error
 * reports and the source/quote/notes/join copy options need book and line ids, so they are left out.
 */
export function useFastDocViewContextMenu(options: {
  selectAllInContainer: () => void
  paneNavigation: PaneNavigation
}) {
  const hostWindow = useHostWindow()

  function onCopy(): boolean {
    try {
      return hostWindow.document.execCommand('copy')
    } catch {
      return false // clipboard unavailable
    }
  }

  /** As the book view: the clipboard is committed by the synchronous copy, then Word pastes. */
  function onPasteIntoWord(): void {
    if (onCopy()) pasteIntoWord().catch(() => {})
  }

  /** The selection's Hebrew words as a library search, in this tab (the book view's own query rule). */
  function onSearchInRepository(): void {
    const selection = hostWindow.getSelection()
    if (!selection || selection.rangeCount === 0 || selection.isCollapsed) return
    const holder = document.createElement('div')
    holder.appendChild(selection.getRangeAt(0).cloneContents())
    const query = toHebrewSearchQuery(holder.innerHTML)
    if (!query) return
    options.paneNavigation.openBookTarget({ route: '/search', title: t('bookView.copy.searchTabTitle', { query }), searchQuery: query })
  }

  const items: ContextMenuItem[] = [
    { label: t('bookView.selectAll'), action: options.selectAllInContainer, shortcut: 'Ctrl+A' },
    { label: t('bookView.copy.copy'), icon: IconCopy20Regular, action: onCopy, shortcut: 'Ctrl+C' },
    { label: t('bookView.copy.intoWord'), icon: IconDocumentArrowRight20Regular, action: onPasteIntoWord, shortcut: 'Ctrl+V' },
    { label: t('bookView.copy.toSearch'), icon: IconSearch20Regular, action: onSearchInRepository, shortcut: 'Ctrl+Shift+C' },
    { type: 'separator' },
    {
      type: 'submenu',
      label: t('bookView.copy.options'),
      items: [
        ...copyCleanMenuItems(),
      ],
    },
  ]

  return { items, onPasteIntoWord, onSearchInRepository }
}
