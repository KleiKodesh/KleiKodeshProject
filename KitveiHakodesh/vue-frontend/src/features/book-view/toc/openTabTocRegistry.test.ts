import { describe, it, expect } from 'vitest'
import { registerOpenTabToc, findOpenTabToc, type OpenTabToc } from './openTabTocRegistry'

describe('openTabTocRegistry', () => {
  it('returns the open tab entries and forgets them once the tab unregisters', () => {
    const raw = [{ id: 1 }] as never[]
    const owner: OpenTabToc = { bookId: 7, raw, pending: null, view: null }
    const unregister = registerOpenTabToc(owner)
    expect(findOpenTabToc(7)).toBe(raw)
    expect(findOpenTabToc(8)).toBeNull()
    expect(findOpenTabToc(7, owner)).toBeNull()
    unregister()
    expect(findOpenTabToc(7)).toBeNull()
  })
  it('shares an in-flight fetch only while its owner is registered', () => {
    const pending = Promise.resolve([])
    const unregister = registerOpenTabToc({ bookId: 3, raw: null, pending, view: null })
    expect(findOpenTabToc(3)).toBe(pending)
    unregister()
    expect(findOpenTabToc(3)).toBeNull()
  })
})
