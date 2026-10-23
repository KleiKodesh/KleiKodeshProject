import { beforeEach, describe, expect, it, vi } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { getCatalogCardLines, getLibraryName, hasLibraryBadges } from './catalogCardLines'
import type { FsItem } from './useBookCatalog'
import type { CategoryNode } from './bookCatalogTree'
import type { BookRow } from '@/webview-host/queries.types'

// The store imports the host bridge, which probes `window` at load time.
vi.hoisted(() => {
  const g = globalThis as Record<string, unknown>
  g.window = globalThis
  g.document = { documentElement: { style: { setProperty() {} }, setAttribute() {}, removeAttribute() {}, getAttribute: () => null }, getElementById: () => null, createElement: () => ({}), head: { appendChild() {} } }
})

const LABEL = 'otzarya-unofficial-books-plain'
const STORAGE_KEY = 'kitvei-hakodesh.books.showLibraryBadge'

function memoryStorage(): Storage {
  const map = new Map<string, string>()
  return {
    get length() { return map.size },
    clear: () => map.clear(),
    getItem: (k) => map.get(k) ?? null,
    key: (i) => [...map.keys()][i] ?? null,
    removeItem: (k) => void map.delete(k),
    setItem: (k, v) => void map.set(k, String(v)),
  }
}

function folder(title: string, label?: string | null): FsItem {
  const node: CategoryNode = {
    id: 1, parentId: null, title, level: 0, libraryLabel: label,
    children: [], books: [], subtreeBookIds: [1, 2, 3],
  }
  return { uid: `f-${title}`, kind: 'folder', node }
}
function book(title: string, label?: string | null): FsItem {
  const row: BookRow = { id: 1, categoryId: 1, title, authors: 'מחבר', libraryLabel: label }
  return { uid: `b-${title}`, kind: 'book', book: row }
}

beforeEach(() => {
  ;(globalThis as { localStorage?: Storage }).localStorage = memoryStorage()
  setActivePinia(createPinia())
})

describe('showLibraryBadge setting', () => {
  it('is on by default', () => {
    const settings = useSettingsStore()
    settings.init()
    expect(settings.showLibraryBadge).toBe(true)
  })

  it('is saved when changed and loaded again by the next session', async () => {
    const first = useSettingsStore()
    first.init()
    first.showLibraryBadge = false
    await nextTick()
    expect(localStorage.getItem(STORAGE_KEY)).toBe('false')

    setActivePinia(createPinia())
    const second = useSettingsStore()
    second.init()
    expect(second.showLibraryBadge).toBe(false)
    await nextTick()

    second.showLibraryBadge = true
    await nextTick()
    setActivePinia(createPinia())
    const third = useSettingsStore()
    third.init()
    expect(third.showLibraryBadge).toBe(true)
  })

  it('goes back to shown on reset', async () => {
    const settings = useSettingsStore()
    settings.init()
    settings.showLibraryBadge = false
    await nextTick()
    await settings.reset()
    expect(settings.showLibraryBadge).toBe(true)
  })
})

describe('hiding the badge (card data shared by the Cards view)', () => {
  const items = [folder('הלכה', LABEL), book('ספר', LABEL), book('רגיל')] as [FsItem, FsItem, FsItem]

  it('shows the badge by default', () => {
    expect(getCatalogCardLines(items[0]).badge).toBe(LABEL)
    expect(getCatalogCardLines(items[1], true).badge).toBe(LABEL)
    expect(hasLibraryBadges(items)).toBe(true)
  })

  it('empties the visible badge for folders and books, and stops reserving the line', () => {
    expect(getCatalogCardLines(items[0], false)).toEqual({ path: '3 ספרים', badge: '' })
    expect(getCatalogCardLines(items[1], false)).toEqual({ path: 'מחבר', badge: '' })
    expect(hasLibraryBadges(items, false)).toBe(false)
  })

  it('still knows the library, so the tooltip can name it', () => {
    expect(getLibraryName(items[0])).toBe(LABEL)
    expect(getLibraryName(items[1])).toBe(LABEL)
    expect(getLibraryName(items[2])).toBe('')
  })
})

// The three views are Vue components and this project runs vitest without a DOM, so the
// wiring of each view to the setting is guarded at source level; the rendering itself is
// verified in a real browser (see the PR notes).
describe('every view honours the setting', () => {
  const read = (rel: string) => readFileSync(fileURLToPath(new URL(rel, import.meta.url)), 'utf8')
  const cards = read('./BookCatalogView.Cards.vue')
  const list = read('./BookCatalogView.List.vue')
  const tiles = read('./BookCatalogView.Tiles.vue')
  const card = read('../home/HomeSuggestionCard.vue')
  const dialog = read('../settings/SettingsAdditionalLibrariesDialog.vue')

  it('Cards passes the preference to the card data', () => {
    expect(cards).toMatch(/getCatalogCardLines\(item,\s*showLibraryBadge\.value\)/)
    expect(cards).toMatch(/hasLibraryBadges\(props\.items,\s*showLibraryBadge\.value\)/)
  })

  it('List only draws the visible tag while the preference is on, and keeps the tooltip', () => {
    expect(list).toMatch(/v-if="showLibraryBadge && libraryBadgeText\(/)
    expect(list).toMatch(/libraryTooltipText\(getLibraryLabel\(item\)\)/)
  })

  it('Tiles draws no badge at all: the tooltip is its only surface and is never hidden', () => {
    expect(tiles).not.toMatch(/library-tag|card-badge/)
    expect(tiles).toMatch(/ספרייה נוספת:/)
  })

  it('the card tooltip names the library even when the badge is hidden', () => {
    expect(card).toMatch(/sourceLibrary \?\? props\.card\.badge/)
  })

  it('the dialog binds a toggle to the setting', () => {
    expect(dialog).toMatch(/v-model="showLibraryBadge"/)
    expect(dialog).toContain('הצג תווית ספרייה בכרטיסים')
  })
})
