// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest'

// useGlobalSearch pulls in stores/services; only the pure prefix parser is under test.
vi.mock('@/stores/booksDataStore', () => ({ useBooksDataStore: () => ({}) }))
vi.mock('@/stores/settingsStore', () => ({ useSettingsStore: () => ({}) }))

const { parseQueryPrefix } = await import('./useGlobalSearch')

const KOVETZ = 'קובץ'
const HIBRU = 'היברו'

describe('parseQueryPrefix English aliases', () => {
  it('routes English file aliases like the Hebrew one', () => {
    expect(parseQueryPrefix(`${KOVETZ}: pdf`)).toEqual({ priority: 'files', effectiveQuery: 'pdf' })
    for (const q of ['file: pdf', 'files pdf', 'FILE:pdf', 'pc: pdf', 'Computer pdf']) {
      expect(parseQueryPrefix(q)).toEqual({ priority: 'files', effectiveQuery: 'pdf' })
    }
  })

  it('routes English HebrewBooks aliases like the Hebrew one', () => {
    expect(parseQueryPrefix(`${HIBRU}: x`)).toEqual({ priority: 'hebrewbooks', effectiveQuery: 'x' })
    for (const q of ['hebrewbooks: x', 'Hebrew Books x', 'HB: x', 'hb x']) {
      expect(parseQueryPrefix(q)).toEqual({ priority: 'hebrewbooks', effectiveQuery: 'x' })
    }
  })

  it('ignores aliases that are only the start of a longer word', () => {
    for (const q of ['filename pdf', 'hbx', 'pcs', 'computers']) {
      expect(parseQueryPrefix(q)).toEqual({ priority: 'catalog', effectiveQuery: q })
    }
  })

  it('treats addins as a files-first term that survives', () => {
    expect(parseQueryPrefix('addins: x')).toEqual({ priority: 'files', effectiveQuery: 'addins: x' })
  })
})
