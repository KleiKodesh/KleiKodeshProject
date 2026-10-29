import { t } from '@/i18n'
import type { ContextMenuItem } from '@/components/ContextMenu.vue'
import type { PaneNavigation } from '@/composables/usePaneNavigation'
import { useSettingsStore } from '@/stores/settingsStore'
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
  const settingsStore = useSettingsStore()

  function onCopy(): boolean {
    try {
      return document.execCommand('copy')
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
    const selection = window.getSelection()
    if (!selection || selection.rangeCount === 0 || selection.isCollapsed) return
    const holder = document.createElement('div')
    holder.appendChild(selection.getRangeAt(0).cloneContents())
    const query = toHebrewSearchQuery(holder.innerHTML)
    if (!query) return
    options.paneNavigation.updateActiveTab({ route: '/search', title: t('bookView.copy.searchTabTitle', { query }), searchQuery: query })
  }

  const items: ContextMenuItem[] = [
    { label: t('bookView.selectAll'), action: options.selectAllInContainer, shortcut: 'Ctrl+A' },
    { label: t('bookView.copy.copy'), action: onCopy, shortcut: 'Ctrl+C' },
    { label: t('bookView.copy.intoWord'), action: onPasteIntoWord, shortcut: 'Ctrl+V' },
    { label: t('bookView.copy.toSearch'), action: onSearchInRepository, shortcut: 'Ctrl+Shift+C' },
    { type: 'separator' },
    {
      type: 'submenu',
      label: t('bookView.copy.options'),
      items: [
        {
          type: 'checkbox',
          label: t('bookView.copy.plain'),
          get checked() { return settingsStore.copyCleanText },
          onChange: (value: boolean) => { settingsStore.copyCleanText = value },
        },
      ],
    },
  ]

  return { items, onPasteIntoWord, onSearchInRepository }
}
