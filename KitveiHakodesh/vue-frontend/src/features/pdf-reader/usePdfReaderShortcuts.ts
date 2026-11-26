import { type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import type { PdfScroller } from './usePdfScroller'
import type { PdfSideMode } from './PdfReaderSideModes.vue'
import type { PdfFindOptionsModel } from './PdfFindOptions.vue'

/**
 * Every PDF view control's key, as its tooltip shows it (the book view's "Label (Key)").
 * Bare letters: PDF.js / Acrobat-style single keys, ignored while typing.
 * Alt+letter: keys for the floating bars and search options, which also work while typing.
 * Ctrl+Left / Ctrl+Right: next / previous outline entry, as the book view's sections.
 * Not used (the app's): Ctrl + W X B J K(TOC here too) U F N E T G H L M, Ctrl+digit,
 * Ctrl+Shift+digit, Alt+W, Alt+X, Alt+arrows, F1 F4 F8 F9 F11.
 */
export const PDF_KEYS = {
  toc: 'Ctrl+K',
  firstPage: 'Home',
  lastPage: 'End',
  prevPage: 'P',
  nextPage: 'N',
  goToPage: 'Ctrl+Alt+G',
  search: 'Ctrl+F',
  handTool: 'H',
  selectTool: 'S',
  edit: 'E',
  ocr: 'O',
  zoomIn: 'Ctrl+=',
  zoomOut: 'Ctrl+-',
  fitWidth: 'Ctrl+0',
  fitCycle: 'F',
  scrollCycle: 'V',
  spreadCycle: 'D',
  rotateLeft: 'Shift+R',
  rotateRight: 'R',
  presentation: 'Ctrl+Alt+P',
  organizer: 'G',
  properties: 'Ctrl+D',
  print: 'Ctrl+P',
  save: 'Ctrl+S',
  saveAs: 'Ctrl+Shift+S',
  addBookmark: 'Ctrl+Shift+D',
  findNext: 'F3',
  findPrev: 'Shift+F3',
  nextSection: 'Ctrl+Left',
  prevSection: 'Ctrl+Right',
  // The app's tab Back / Forward keys; in the PDF view they first step back through its jumps.
  historyBack: 'Alt+Right',
  historyForward: 'Alt+Left',
  side: { outline: 'Alt+1', bookmarks: 'Alt+2', comments: 'Alt+3', thumbnails: 'Alt+4' } as Record<PdfSideMode, string>,
  editTool: { highlight: 'Alt+H', text: 'Alt+T', draw: 'Alt+D', image: 'Alt+I', signature: 'Alt+G', note: 'Alt+N' },
  ocrScript: 'Alt+S',
  ocrForce: 'Alt+F',
  find: { highlightAll: 'Alt+A', caseSensitive: 'Alt+C', entireWord: 'Alt+E', matchDiacritics: 'Alt+M' } as Record<keyof PdfFindOptionsModel, string>,
} as const

/** "Label (Key)" - the book view's tooltip form; a label that already names its key is left as is. */
export const withKey = (label: string, key: string) => (label.includes(`(${key})`) ? label : `${label} (${key})`)

const SIDE_KEYS: Record<string, PdfSideMode> = { Digit1: 'outline', Digit2: 'bookmarks', Digit3: 'comments', Digit4: 'thumbnails' }
export type PdfEditTool = 'highlight' | 'text' | 'draw' | 'image' | 'signature' | 'note'
const EDIT_KEYS: Record<string, PdfEditTool> = { KeyH: 'highlight', KeyT: 'text', KeyD: 'draw', KeyI: 'image', KeyG: 'signature', KeyN: 'note' }
const FIND_KEYS: Record<string, keyof PdfFindOptionsModel> = { KeyA: 'highlightAll', KeyC: 'caseSensitive', KeyE: 'entireWord', KeyM: 'matchDiacritics' }

/**
 * The PDF view's keyboard: PDF.js's own viewer keys (as the old viewer had them) plus the
 * app's copy keys. Bare keys, Alt keys and the selection keys act while focus is inside this
 * viewer; the Ctrl commands (search, TOC, print, save, properties, bookmark, go to page,
 * presentation, sections) and F3 act while this PDF tab is the window's active tab, wherever
 * focus is (as the book view's Ctrl keys) - except inside a dialog or another search field. Zoom keys: usePdfZoomInput;
 * keys inside presentation mode: usePdfPresentation.
 *
 * Not taken from PDF.js: Ctrl+G / Ctrl+Shift+G (find again - the app's go-home key; Enter in the
 * search bar steps matches), F4 (side panel - the app's keep-screen-on key; Ctrl+K here),
 * Ctrl+O (open file - the app's Ctrl+4), Shift+Up/Down (caret browsing).
 */
export function usePdfReaderShortcuts(
  root: Ref<HTMLElement | null>,
  scroller: Ref<HTMLElement | null>,
  pdf: PdfScroller,
  actions: {
    inPresentation: () => boolean
    openSearch: () => void
    closeSearch: () => boolean
    toggleToc: () => void
    presentation: () => void
    print: () => void
    save: () => void
    saveAs: () => void
    setHandTool: (on: boolean) => void
    selectedText: () => string
    copyIntoWord: () => void
    searchSelection: () => void
    toggleEdit: () => void
    closeEditBar: () => boolean
    editVisible: () => boolean
    editTool: (tool: PdfEditTool) => void
    toggleOcr: () => void
    ocrActive: () => boolean
    cycleOcrScript: () => void
    toggleOcrForce: () => void
    searchVisible: () => boolean
    toggleFindOption: (option: keyof PdfFindOptionsModel) => void
    cycleFit: () => void
    cycleScroll: () => void
    cycleSpread: () => void
    toggleOrganizer: () => void
    properties: () => void
    addBookmark: () => void
    showSideMode: (mode: PdfSideMode) => void
    /** F3 / Shift+F3: the next / previous match; false when there is no search to step. */
    findAgain: (previous: boolean) => boolean
    /** Ctrl+Left / Ctrl+Right: the next / previous outline entry (RTL, as the book view). */
    adjacentSection: (direction: 'next' | 'previous') => void
    /**
     * Undo / redo of PDF.js's edits (a deleted comment, a note...) where PDF.js's own keys do not
     * reach: in reading mode (it listens only while an editor is on) and in an extra window (it
     * listens on the main window). False when PDF.js takes the key itself.
     */
    undoRedo: (redo: boolean) => boolean
  },
) {
  const hostWindow = useHostWindow()
  const paneNavigation = usePaneNavigation()

  // Alt+Right / Alt+Left (the app's tab Back / Forward, useAppTitleBarShortcuts): first back and
  // forward through this document's jumps (links, outline, bookmarks, search), while there are any.
  useEventListener(root, 'localhistory', (e: Event) => {
    const back = (e as CustomEvent<{ back: boolean }>).detail?.back
    if (back ? pdf.jumpHistory.goBack() : pdf.jumpHistory.goForward()) e.preventDefault()
  })

  const isTyping = (el: Element | null) =>
    !!el && (el.matches('input, textarea, select') || (el as HTMLElement).isContentEditable)

  const turn = (delta: 1 | -1) => pdf.goToPage(pdf.pageNumber.value + delta)
  const pageFit = () => pdf.preset.value === 'page-fit'
  const hasVScroll = () => !!scroller.value && scroller.value.scrollHeight > scroller.value.clientHeight + 1
  const hasHScroll = () => !!scroller.value && scroller.value.scrollWidth > scroller.value.clientWidth + 1

  useEventListener(hostWindow, 'keydown', (e: KeyboardEvent) => {
    const active = hostWindow.document.activeElement
    if (!root.value || !active || !root.value.isConnected) return
    // Focus in the viewer, or nowhere (the body, after a click on a non-focusable spot).
    const inside = root.value.contains(active) || active === hostWindow.document.body
    // Elsewhere in the window (the title bar, the address bar) while this tab shows: the Ctrl
    // commands only, never inside a dialog or another view's search field.
    const pageWide = inside || (paneNavigation.activeTab.route === '/pdf-view'
      && !active.closest('[role="dialog"], dialog, [data-ctrlf-enabled]'))
    if (!pageWide) return
    const ctrl = e.ctrlKey || e.metaKey
    const typing = isTyping(active)
    let handled = true

    if (!inside && !(ctrl && /^Key[FKPSDG]$/.test(e.code)) && e.code !== 'F3' && !(ctrl && !e.shiftKey && !e.altKey && (e.code === 'ArrowLeft' || e.code === 'ArrowRight'))) {
      return
    } else if (ctrl && !e.altKey && !typing && (e.code === 'KeyZ' || (e.code === 'KeyY' && !e.shiftKey))) {
      // Ctrl+Z, Ctrl+Y / Ctrl+Shift+Z (PDF.js's keys)
      handled = actions.undoRedo(e.code === 'KeyY' || e.shiftKey)
    } else if (!ctrl && e.altKey && !e.shiftKey) {
      // Alt+letter / Alt+digit: also while typing (the search box, a text box on the page).
      const side = SIDE_KEYS[e.code]
      const tool = EDIT_KEYS[e.code]
      const find = FIND_KEYS[e.code]
      if (side) actions.showSideMode(side)
      else if (tool && (tool === 'note' || actions.editVisible())) actions.editTool(tool)
      else if (find && actions.searchVisible()) actions.toggleFindOption(find)
      else if (e.code === 'KeyS' && actions.ocrActive()) actions.cycleOcrScript()
      else if (e.code === 'KeyF' && actions.ocrActive()) actions.toggleOcrForce()
      else handled = false
    } else if (ctrl && e.shiftKey && !e.altKey && e.code === 'KeyS') {
      actions.saveAs()
    } else if (ctrl && e.shiftKey && !e.altKey && e.code === 'KeyD') {
      actions.addBookmark()
    } else if (ctrl && e.altKey && !e.shiftKey) {
      if (e.code === 'KeyP') actions.presentation()
      else if (e.code === 'KeyG') {
        const input = root.value.querySelector<HTMLInputElement>('input.page-input')
        input?.focus()
        input?.select()
      } else handled = false
    } else if (ctrl && !e.altKey && !e.shiftKey) {
      switch (e.code) {
        case 'KeyF': actions.openSearch(); break
        case 'KeyK': actions.toggleToc(); break
        case 'KeyP': actions.print(); break
        case 'KeyS': actions.save(); break
        case 'KeyD': actions.properties(); break
        // RTL, as the book view: Left = next section, Right = previous. Fields keep word moves.
        case 'ArrowLeft': if (typing) handled = false; else actions.adjacentSection('next'); break
        case 'ArrowRight': if (typing) handled = false; else actions.adjacentSection('previous'); break
        case 'ArrowUp': if (typing) handled = false; else pdf.jumpToPage(1); break
        case 'ArrowDown': if (typing) handled = false; else pdf.jumpToPage(pdf.pageCount.value); break
        // Copy into Word, as in the book view and documents (only with text selected in the pages).
        case 'KeyV': if (typing || !actions.selectedText().trim()) handled = false; else actions.copyIntoWord(); break
        default: handled = false
      }
    } else if (ctrl && e.shiftKey && !e.altKey && e.code === 'KeyC') {
      if (typing || !actions.selectedText().trim()) handled = false
      else actions.searchSelection()
    } else if (!ctrl && !e.altKey) {
      if (e.code === 'F3') {
        // Also while typing in the search box; the browser's own F3 (find in page) never.
        actions.findAgain(e.shiftKey)
      } else if (e.code === 'Escape') {
        handled = actions.closeSearch() || actions.closeEditBar()
      } else if (typing || e.defaultPrevented || actions.inPresentation()) {
        handled = false
      } else if (e.shiftKey) {
        if (e.code === 'KeyR') pdf.rotate(-90)
        else if (e.code === 'Space' && pageFit()) turn(-1)
        else handled = false
      } else {
        switch (e.code) {
          case 'KeyN': case 'KeyJ': turn(1); break
          case 'KeyP': case 'KeyK': turn(-1); break
          case 'ArrowDown': case 'PageDown': if (!hasVScroll() || pageFit()) turn(1); else handled = false; break
          case 'ArrowUp': case 'PageUp': if (!hasVScroll() || pageFit()) turn(-1); else handled = false; break
          case 'ArrowRight': if (!hasHScroll() || pageFit()) turn(1); else handled = false; break
          case 'ArrowLeft': if (!hasHScroll() || pageFit()) turn(-1); else handled = false; break
          case 'Space': if (pageFit()) turn(1); else handled = false; break
          case 'Backspace': if (pageFit()) turn(-1); else handled = false; break
          case 'Home': pdf.jumpToPage(1); break
          case 'End': pdf.jumpToPage(pdf.pageCount.value); break
          case 'KeyR': pdf.rotate(90); break
          case 'KeyH': actions.setHandTool(true); break
          case 'KeyS': actions.setHandTool(false); break
          case 'KeyE': actions.toggleEdit(); break
          case 'KeyO': actions.toggleOcr(); break
          case 'KeyF': actions.cycleFit(); break
          case 'KeyV': actions.cycleScroll(); break
          case 'KeyD': actions.cycleSpread(); break
          case 'KeyG': actions.toggleOrganizer(); break
          default: handled = false
        }
      }
    } else {
      handled = false
    }
    if (handled) e.preventDefault()
  })
}
