// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createApp, ref, nextTick } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

// Same host stubs as tabStore.test.ts: a native-tabs host is what makes openTab add
// a tab (the single-tab path navigates in place instead).
vi.mock('@/webview-host/bridge', () => ({
  hasNativeChromeTabs: true,
  navigatesDestinationsInPlace: false,
  disposeLocalFileHost: vi.fn(),
  getHbLocalFolderFromRegistry: vi.fn(async () => null),
  setHbLocalFolderInRegistry: vi.fn(async () => undefined),
}))
vi.mock('@/webview-host/seforimApi', () => ({
  getWordLinkTargetsForBook: vi.fn(async () => null),
}))
vi.mock('@/stores/recentlyOpenedStore', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/stores/recentlyOpenedStore')>()
  return { ...original, useRecentlyOpenedStore: () => ({ trackNavigation: vi.fn() }) }
})

const idb = vi.hoisted(() => ({ tabPuts: [] as string[], lastReadPuts: [] as string[] }))
vi.mock('@/utils/persistence', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/utils/persistence')>()
  return {
    ...original,
    idbTabsGet: async () => null,
    idbTabsSet: async (key: string) => { idb.tabPuts.push(key) },
    idbTabsDelete: async () => undefined,
    idbTabsDeleteByPrefix: async () => undefined,
    dbGet: async () => null,
    dbSet: async (db: string, key: string) => { if (db === 'app-lastread') idb.lastReadPuts.push(key) },
    dbDelete: async () => undefined,
    dbCount: async () => 0,
    dbListEntries: async () => [],
  }
})

import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useBookViewLinesScroll } from './useBookViewLinesScroll'

const BOOK_ID = 7

/** Mounts the composable the way BookViewLinesContent does, on a scroller sitting at line 3. */
function mountScroll(
  tabId: string,
  props: Parameters<typeof useBookViewLinesScroll>[1] = {},
  rowElement: (index: number) => HTMLElement | null = () => null,
) {
  const scrollerElement = document.createElement('div')
  document.body.appendChild(scrollerElement)
  const scroller = {
    scrollerElement,
    scrollbarElement: null,
    renderedRange: { start: 0, end: 10 },
    scrollToIndex: vi.fn(async () => undefined),
    revealIndex: vi.fn(),
    readFirstVisible: () => ({ index: 3, offsetWithinRow: 0 }),
    isRowRendered: () => true,
    isRowFullyVisible: () => true,
    rowElement,
    barTargetAt: () => null,
    replaceRows: vi.fn(),
    nudge: vi.fn(),
    landing: false,
    isLanding(): boolean { return this.landing },
  }
  let scroll!: ReturnType<typeof useBookViewLinesScroll>
  const app = createApp({
    setup() {
      scroll = useBookViewLinesScroll(
        () => scroller,
        props,
        { tabStore: useTabStore(), bookViewStore: useBookViewStore(), autoSelectTopLine: ref(false), zoom: ref(100), tabId, bookId: BOOK_ID },
        () => undefined,
      )
      return () => null
    },
  })
  app.mount(document.createElement('div'))
  return { app, scroll, scrollerElement, scroller }
}

/**
 * The position save runs from two places when a book view leaves: the tab close
 * captures it while the view is still mounted (recents, last-read), and the view's
 * own unmount saves again. For a closed tab the second must not repeat the first.
 */
describe('useBookViewLinesScroll position save on unmount', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    idb.tabPuts.length = 0
    idb.lastReadPuts.length = 0
  })

  it('a tab close writes last-read exactly once: the capture, not the unmount after it', async () => {
    const store = useTabStore()
    const tab = store.openTab({ route: '/book-view', title: 'book', bookId: BOOK_ID })
    const { app } = mountScroll(tab.id)

    store.closeTab(tab.id)
    expect(idb.lastReadPuts).toEqual([`lastread:${BOOK_ID}`])

    app.unmount()
    await nextTick()
    expect(idb.lastReadPuts).toEqual([`lastread:${BOOK_ID}`])
    // The capture's per-tab put (deleted by the close right after); the unmount adds none.
    expect(idb.tabPuts.filter((k) => k.startsWith(`book:default:${tab.id}:`))).toEqual([`book:default:${tab.id}:${BOOK_ID}`])
  })

  it('a close inside a suppressed window (mid programmatic jump) saves on unmount instead', async () => {
    const store = useTabStore()
    const tab = store.openTab({ route: '/book-view', title: 'book', bookId: BOOK_ID })
    const { app, scroller } = mountScroll(tab.id)

    scroller.landing = true
    store.closeTab(tab.id)
    expect(idb.lastReadPuts).toEqual([])

    app.unmount()
    await nextTick()
    expect(idb.lastReadPuts).toEqual([`lastread:${BOOK_ID}`])
  })

  it('a view leaving while its tab stays open still saves on unmount', async () => {
    const store = useTabStore()
    const tab = store.openTab({ route: '/book-view', title: 'book', bookId: BOOK_ID })
    const { app } = mountScroll(tab.id)

    app.unmount()
    await nextTick()
    expect(idb.lastReadPuts).toEqual([`lastread:${BOOK_ID}`])
    expect(idb.tabPuts).toContain(`book:default:${tab.id}:${BOOK_ID}`)
  })
})

/**
 * The keyboard caret after an opening. Caret browsing (F7) navigates from the
 * document's collapsed selection, and an opening that only focused the viewport left
 * it wherever the browser chose — the top of the mounted window, rows above the
 * start row — so the first arrow key moved away from the line that had just been opened
 * (the locate bar and a search result both open this way). See lineCaret.ts.
 */
describe('useBookViewLinesScroll opening caret', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    document.body.innerHTML = ''
    window.getSelection()?.removeAllRanges()
  })

  /** jsdom has no layout: give an element the box the caret code measures. */
  function box(element: HTMLElement, top: number, bottom: number) {
    element.getBoundingClientRect = () =>
      ({ top, bottom, left: 0, right: 600, width: 600, height: bottom - top, x: 0, y: top, toJSON: () => ({}) }) as DOMRect
  }

  /** A rendered line inside the scroller's viewport, at the given box. */
  function lineIn(viewport: HTMLElement, text: string, top: number, bottom: number): HTMLElement {
    const row = document.createElement('div')
    const line = document.createElement('div')
    line.className = 'line'
    line.textContent = text
    box(line, top, bottom)
    row.appendChild(line)
    viewport.appendChild(row)
    return line
  }

  it('puts the caret on the first line in view, not on the rows mounted above it', () => {
    const store = useTabStore()
    const tab = store.openTab({ route: '/book-view', title: 'book', bookId: BOOK_ID })
    const { app, scroll, scrollerElement } = mountScroll(tab.id, { initialLineIndex: 1200 })
    // The viewport's top edge is at 100: one mounted row lies wholly above it (the
    // window starts some rows before the start row), the opened line is at the top.
    box(scrollerElement, 100, 700)
    lineIn(scrollerElement, 'ויצא יעקב מבאר שבע', 40, 90)
    const opened = lineIn(scrollerElement, 'וישלח יעקב מלאכים לפניו', 100, 150)

    scroll.onScrollerOpened()

    const selection = window.getSelection()!
    expect(selection.rangeCount).toBe(1)
    expect(selection.isCollapsed).toBe(true)
    expect(opened.contains(selection.anchorNode)).toBe(true)
    app.unmount()
  })

  it('an opening at the top seats the caret on the first line too', () => {
    const store = useTabStore()
    const tab = store.openTab({ route: '/book-view', title: 'book', bookId: BOOK_ID })
    const { app, scroll, scrollerElement } = mountScroll(tab.id)
    box(scrollerElement, 100, 700)
    const first = lineIn(scrollerElement, 'בראשית ברא', 100, 150)

    scroll.onScrollerOpened()

    expect(first.contains(window.getSelection()!.anchorNode)).toBe(true)
    app.unmount()
  })

  it('leaves the document selection alone while no line is rendered', () => {
    const store = useTabStore()
    const tab = store.openTab({ route: '/book-view', title: 'book', bookId: BOOK_ID })
    const { app, scroll } = mountScroll(tab.id)

    scroll.onScrollerOpened()

    expect(window.getSelection()!.rangeCount).toBe(0)
    app.unmount()
  })
})
