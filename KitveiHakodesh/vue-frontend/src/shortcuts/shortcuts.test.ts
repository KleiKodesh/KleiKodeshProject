import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'

const ls = vi.hoisted(() => ({ data: new Map<string, unknown>() }))
vi.mock('@/utils/persistence', () => ({
  lsGet: (key: string) => (ls.data.has(key) ? JSON.parse(JSON.stringify(ls.data.get(key))) : null),
  lsSet: (key: string, value: unknown) => { ls.data.set(key, JSON.parse(JSON.stringify(value))) },
}))
// The Word task pane: no native tab strip, VSTO host.
vi.mock('@/webview-host/bridge', () => ({ hasNativeChromeTabs: false, isVstoEnvironment: true }))

import { comboFromEvent, comboProblem, isWellFormedCombo, makeCombo, parseCombo, UNREACHABLE_IN_WORD } from './shortcutCombo'
import { SHORTCUTS, SHORTCUTS_BY_ID, scopesOverlap } from './shortcutRegistry'
import { useShortcutsStore } from './shortcutsStore'

const key = (code: string, mods: { ctrl?: boolean; alt?: boolean; shift?: boolean } = {}) =>
  ({ code, ctrlKey: !!mods.ctrl, altKey: !!mods.alt, shiftKey: !!mods.shift, metaKey: false }) as KeyboardEvent

describe('combinations', () => {
  it('are built from the physical key, modifiers in a fixed order', () => {
    expect(comboFromEvent(key('KeyJ', { shift: true, ctrl: true }))).toBe('Ctrl+Shift+KeyJ')
    expect(comboFromEvent(key('F8'))).toBe('F8')
    expect(comboFromEvent(key('ControlLeft', { ctrl: true }))).toBeNull()
    expect(parseCombo('Ctrl+Alt+ArrowLeft')).toEqual({ ctrl: true, alt: true, shift: false, code: 'ArrowLeft' })
  })

  it('reject keys that would steal typing, editing keys and Windows keys', () => {
    expect(comboProblem('KeyA')).toBe('needsModifier')
    expect(comboProblem('Shift+KeyA')).toBe('needsModifier')
    expect(comboProblem('Enter')).toBe('needsModifier')
    expect(comboProblem('F4')).toBeNull()
    expect(comboProblem('Shift+F5')).toBeNull()
    expect(comboProblem('Ctrl+KeyC')).toBe('editing')
    expect(comboProblem('Alt+F4')).toBe('system')
    expect(comboProblem('Ctrl+Alt+KeyQ')).toBeNull()
  })

  it('recognise only well-formed strings read back from storage', () => {
    expect(isWellFormedCombo('Ctrl+KeyJ')).toBe(true)
    expect(isWellFormedCombo('Shift+Ctrl+KeyJ')).toBe(false)
    expect(isWellFormedCombo('Ctrl+')).toBe(false)
    expect(isWellFormedCombo(42)).toBe(false)
  })
})

describe('registry', () => {
  it('has unique ids and well-formed defaults', () => {
    expect(new Set(SHORTCUTS.map((d) => d.id)).size).toBe(SHORTCUTS.length)
    for (const d of SHORTCUTS) for (const c of d.defaults) expect(isWellFormedCombo(c), `${d.id} ${c}`).toBe(true)
  })

  it('has no clashing defaults among the shortcuts that exist in one host', () => {
    const active = SHORTCUTS.filter((d) => d.available !== false)
    for (const a of active) for (const b of active) {
      if (a.id >= b.id || !scopesOverlap(a.scope, b.scope)) continue
      const shared = a.defaults.filter((c) => b.defaults.includes(c))
      expect(shared, `${a.id} / ${b.id}`).toEqual([])
    }
  })

  it('knows which default does not work inside Word', () => {
    expect(UNREACHABLE_IN_WORD.has(SHORTCUTS_BY_ID.get('book.commentaryRight')!.defaults[0]!)).toBe(true)
  })
})

describe('shortcuts store', () => {
  const fresh = () => { setActivePinia(createPinia()); return useShortcutsStore() }
  beforeEach(() => ls.data.clear())

  it('matches a keydown to its id, and the same key to two pages that never overlap', () => {
    const s = fresh()
    expect(s.idsFor(key('KeyG', { ctrl: true }))).toEqual(['nav.home'])
    expect([...s.idsFor(key('KeyK', { ctrl: true }))].sort()).toEqual(['book.toc', 'doc.toc'])
    expect(s.idsFor(key('KeyG'))).toEqual([])
  })

  it('stores only changes, and a change survives a reload', async () => {
    const s = fresh()
    s.assign('nav.home', makeCombo('KeyQ', { ctrl: true, alt: true }))
    await nextTick()
    expect(ls.data.get('shortcuts.bindings')).toEqual({ 'nav.home': ['Ctrl+Alt+KeyQ'] })
    const again = fresh()
    expect(again.idsFor(key('KeyQ', { ctrl: true, alt: true }))).toEqual(['nav.home'])
    expect(again.idsFor(key('KeyG', { ctrl: true }))).toEqual([])
  })

  it('reports conflicts only where both can be active', () => {
    const s = fresh()
    // Ctrl+K: book.toc lives in the book view, so a document shortcut may share it ...
    expect(s.conflictsFor('doc.search', 'Ctrl+KeyK').map((c) => c.def.id)).toEqual(['doc.toc'])
    // ... but a pane-wide one collides with both.
    expect(s.conflictsFor('nav.home', 'Ctrl+KeyK').map((c) => c.def.id).sort()).toEqual(['book.toc', 'doc.toc'])
    // Fixed rows count.
    expect(s.conflictsFor('nav.home', 'Ctrl+Tab').some((c) => c.def.fixed)).toBe(true)
  })

  it('moving a combination takes it from the other shortcut', () => {
    const s = fresh()
    s.assign('nav.home', 'Ctrl+KeyL')
    expect(s.combosFor('view.theme')).toEqual([])
    expect(s.idsFor(key('KeyL', { ctrl: true }))).toEqual(['nav.home'])
  })

  it('restore keeps a default that another shortcut has taken since', () => {
    const s = fresh()
    s.assign('nav.home', 'Ctrl+KeyL') // view.theme loses Ctrl+L
    const taken = s.reset('view.theme')
    expect(taken.map((c) => c.def.id)).toEqual(['nav.home'])
    expect(s.combosFor('view.theme')).toEqual([])
    s.resetAll()
    expect(s.combosFor('view.theme')).toEqual(['Ctrl+KeyL'])
    expect(s.hasOverrides).toBe(false)
  })

  it('ignores junk and fixed rows read back from storage', () => {
    ls.data.set('shortcuts.bindings', { 'nav.home': ['Ctrl+KeyQ', 'bogus+'], 'tabs.cycle': ['Ctrl+KeyY'], 'no.such': ['F2'] })
    const s = fresh()
    expect(s.combosFor('nav.home')).toEqual(['Ctrl+KeyQ'])
    expect(s.combosFor('tabs.cycle')).toEqual(['Ctrl+Tab'])
  })

  it('in the Word pane, Alt+W also removes the current page (no tab strip)', () => {
    const s = fresh()
    expect(s.idsFor(key('KeyW', { alt: true }))).toEqual(['tabs.close'])
  })

  it('personal "open this book" shortcuts match, collide and survive a reload', async () => {
    const s = fresh()
    const id = s.addPersonalBook(42, 'משנה ברורה')
    expect(s.combosFor(id)).toEqual([])
    // Ctrl+G is "home": moving it asks first, then takes it from home.
    expect(s.conflictsFor(id, 'Ctrl+KeyG').map((c) => c.def.id)).toEqual(['nav.home'])
    s.assign(id, 'Ctrl+KeyG')
    expect(s.idsFor(key('KeyG', { ctrl: true }))).toEqual([id])
    expect(s.combosFor('nav.home')).toEqual([])
    // Another shortcut sees the personal one as a conflict.
    expect(s.conflictsFor('view.theme', 'Ctrl+KeyG').map((c) => c.def.id)).toEqual([id])
    await nextTick()
    const again = fresh()
    expect(again.personal.map((p) => [p.bookId, p.combo])).toEqual([[42, 'Ctrl+KeyG']])
    expect(again.idsFor(key('KeyG', { ctrl: true }))).toEqual([id])
  })

  it('restore all gives a default key back and clears it from the personal shortcut', () => {
    const s = fresh()
    const id = s.addPersonalBook(7, 'Book')
    s.assign(id, 'Ctrl+KeyG')
    const cleared = s.resetAll()
    expect(cleared.map((p) => p.id)).toEqual([id])
    expect(s.combosFor(id)).toEqual([])
    expect(s.idsFor(key('KeyG', { ctrl: true }))).toEqual(['nav.home'])
    s.removePersonal(id)
    expect(s.personal).toEqual([])
  })

  it('drops malformed personal entries read back from storage', () => {
    ls.data.set('shortcuts.personal', [
      { id: 'personal.a', kind: 'openBook', bookId: 1, title: 'A', combo: 'Ctrl+Alt+KeyA' },
      { id: 'personal.a', kind: 'openBook', bookId: 2, title: 'dup', combo: null },
      { id: 'x', kind: 'openBook', bookId: 3, title: 'bad id', combo: null },
      { id: 'personal.b', kind: 'openBook', bookId: 4, title: 'B', combo: 'junk+' },
    ])
    const s = fresh()
    expect(s.personal.map((p) => [p.id, p.combo])).toEqual([['personal.a', 'Ctrl+Alt+KeyA'], ['personal.b', null]])
  })
})
