// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { effectScope, nextTick, shallowRef } from 'vue'
import { useFastDocViewNotes } from './useFastDocViewNotes'
import type { FastDocViewApi, FastDocViewNote, FastDocViewSummary } from '@/webview-host/fastDocViewApi'

async function settle() {
  for (let i = 0; i < 20; i++) { await Promise.resolve(); await nextTick() }
}

describe('useFastDocViewNotes - provisional notes', () => {
  it('fetches provisional notes again once the index completes, keeping them shown meanwhile', async () => {
    const state = { complete: false, fetches: [] as Array<[number, number]> }
    const note = (index: number): FastDocViewNote => {
      const isProvisional = !state.complete && index === 2
      return { kind: 'footnote', number: index + 1, label: String(index + 1), html: isProvisional ? 'see' : `note ${index}`, ...(isProvisional ? { provisional: true } : {}) }
    }
    const api = {
      noteBlocks: async () => Int32Array.from([0, 1, 2, 3]),
      notes: async (_id: string, first: number, count: number) => {
        state.fetches.push([first, count])
        return Array.from({ length: Math.max(0, Math.min(4, first + count) - first) }, (_, i) => note(first + i))
      },
    } as unknown as FastDocViewApi
    const summary = shallowRef<FastDocViewSummary | null>({ documentId: 'doc', footnoteCount: 4, endnoteCount: 0, isIndexComplete: false } as unknown as FastDocViewSummary)
    const scope = effectScope()
    const notes = scope.run(() => useFastDocViewNotes({ api, summary }))!
    await settle()
    await notes.ensureNotes(0, 4)
    expect(notes.noteAt(2)?.html).toBe('see')
    expect(notes.noteAt(2)?.provisional).toBe(true)
    const version = notes.refetchVersion.value
    state.complete = true
    summary.value = { ...summary.value!, isIndexComplete: true }
    await settle()
    expect(notes.refetchVersion.value).toBe(version + 1)
    expect(notes.noteAt(2)?.html).toBe('see')        // still shown until the new copy arrives
    state.fetches.length = 0
    await notes.ensureNotes(0, 4)
    expect(state.fetches).toEqual([[2, 1]])           // only the provisional note is fetched again
    expect(notes.noteAt(2)?.html).toBe('note 2')
    expect(notes.noteAt(2)?.provisional).toBeUndefined()
    scope.stop()
  })
})
