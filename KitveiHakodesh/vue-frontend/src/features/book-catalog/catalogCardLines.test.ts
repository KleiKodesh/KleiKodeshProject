import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import {
  getCatalogCardLines,
  hasLibraryBadges,
  libraryBadgeText,
  libraryTooltipText,
} from './catalogCardLines'
import type { FsItem } from './useBookCatalog'
import type { CategoryNode } from './bookCatalogTree'
import type { BookRow } from '@/webview-host/queries.types'

const LABEL = 'otzarya-unofficial-books-plain'

function folder(title: string, books: number, libraryLabel?: string | null): FsItem {
  const node: CategoryNode = {
    id: 1,
    parentId: null,
    title,
    level: 0,
    libraryLabel,
    children: [],
    books: [],
    subtreeBookIds: Array.from({ length: books }, (_, i) => i),
  }
  return { uid: `f-${title}`, kind: 'folder', node }
}

function book(title: string, authors: string | null, libraryLabel?: string | null): FsItem {
  const row: BookRow = { id: 1, categoryId: 1, title, authors, libraryLabel }
  return { uid: `b-${title}`, kind: 'book', book: row }
}

describe('getCatalogCardLines', () => {
  it('keeps the library out of the secondary line (the cause of the overlapping text)', () => {
    const lines = getCatalogCardLines(folder('הלכה', 114, LABEL))
    expect(lines.path).toBe('114 ספרים')
    expect(lines.path).not.toContain('otzarya')
    expect(lines.badge).toBe(LABEL)
  })

  it('has no badge for the main library', () => {
    expect(getCatalogCardLines(folder('הלכה', 2533)).badge).toBe('')
    expect(getCatalogCardLines(folder('הלכה', 2533, null)).badge).toBe('')
    expect(getCatalogCardLines(book('ספר', 'מחבר', '   ')).badge).toBe('')
  })

  it('uses authors for a book and an empty path when there are none', () => {
    expect(getCatalogCardLines(book('ספר', 'רש"י', LABEL))).toEqual({ path: 'רש"י', badge: LABEL })
    expect(getCatalogCardLines(book('ספר', null))).toEqual({ path: '', badge: '' })
  })

  it('shows no count for an empty folder', () => {
    expect(getCatalogCardLines(folder('ריק', 0, LABEL)).path).toBe('')
  })
})

describe('hasLibraryBadges', () => {
  it('is true as soon as one item is from an additional library', () => {
    expect(hasLibraryBadges([folder('א', 1), folder('ב', 2, LABEL)])).toBe(true)
    expect(hasLibraryBadges([folder('א', 1), book('ב', 'ג')])).toBe(false)
    expect(hasLibraryBadges([])).toBe(false)
  })
})

describe('libraryBadgeText', () => {
  it('drops a trailing database extension only', () => {
    expect(libraryBadgeText('personal.db')).toBe('personal')
    expect(libraryBadgeText('My.Library.SQLITE')).toBe('My.Library')
    expect(libraryBadgeText(LABEL)).toBe(LABEL)
    expect(libraryBadgeText('db')).toBe('db')
    expect(libraryBadgeText('.db')).toBe('.db')
  })

  it('tolerates missing labels', () => {
    expect(libraryBadgeText(undefined)).toBe('')
    expect(libraryBadgeText(null)).toBe('')
    expect(libraryTooltipText(null)).toBe('')
    expect(libraryTooltipText(LABEL)).toBe(`ספרייה נוספת: ${LABEL}`)
  })
})

// Layout can only be verified in a real browser (see the PR notes); this guards the
// source-level invariants that made the layout break, so they cannot be edited away
// unnoticed: every catalog card line is a single, clipped, fixed-height line.
describe('card line CSS invariants', () => {
  const read = (rel: string) =>
    readFileSync(fileURLToPath(new URL(rel, import.meta.url)), 'utf8')
  const cardSource = read('../home/HomeSuggestionCard.vue')
  const cardsView = read('./BookCatalogView.Cards.vue')

  it('a card is not a popup: it must not be shifted into the viewport (ROOT CAUSE of the stacked cards)', () => {
    // useDropdownClose fits its target into the window by default, writing an inline
    // `translate` that moves any card extending past the window edge up on top of the
    // visible ones. A card in a scrolling grid always does.
    expect(cardSource).toMatch(/useDropdownClose\(wrapRef,[\s\S]*?fitToViewport:\s*false/)
  })

  it('the badge line is nowrap, clipped and a fixed height', () => {
    const rule = /\.card-badge-line\s*\{[^}]*\}/.exec(cardSource)?.[0] ?? ''
    expect(rule).toMatch(/height:\s*16px/)
    expect(rule).toMatch(/white-space:\s*nowrap/)
    expect(rule).toMatch(/overflow:\s*hidden/)
    const badge = /\.card-badge\s*\{[^}]*\}/.exec(cardSource)?.[0] ?? ''
    expect(badge).toMatch(/text-overflow:\s*ellipsis/)
    expect(badge).toMatch(/unicode-bidi:\s*isolate/)
    expect(cardSource).toMatch(/class="card-badge"[^>]*dir="auto"/)
  })

  it('single-line mode clips the path instead of wrapping it', () => {
    const rule = /\.card-path\.is-single-line\s*\{[^}]*\}/.exec(cardSource)?.[0] ?? ''
    expect(rule).toMatch(/white-space:\s*nowrap/)
    expect(rule).toMatch(/text-overflow:\s*ellipsis/)
    expect(rule).toMatch(/height:\s*1\.3em/)
  })

  it('catalog cards always use single-line mode and never concatenate the library', () => {
    expect(cardsView).toMatch(/singleLine:\s*true/)
    expect(cardsView).not.toMatch(/libraryLabel/)
  })
})
