import { describe, expect, it, vi } from 'vitest'

const call = vi.fn(async (anchorIds: number[], noteIds: number[]) => ({
  anchors: anchorIds.map((lineId) => ({ lineId })),
  notes: noteIds.map((lineId) => ({ lineId })),
}))
vi.mock('@/webview-host/seforimApi', () => ({ getLineDecorationsForLines: call }))

const { createLineDecorationsBatch } = await import('./lineDecorationsBatch')

describe('createLineDecorationsBatch', () => {
  it('answers anchors and notes requested in the same turn with one call', async () => {
    const batch = createLineDecorationsBatch()
    const [anchors, notes] = await Promise.all([batch.anchorsFor([1, 2]), batch.notesFor([2, 3])])
    expect(call).toHaveBeenCalledTimes(1)
    expect(call).toHaveBeenCalledWith([1, 2], [2, 3])
    expect(anchors.map((a) => a.lineId)).toEqual([1, 2])
    expect(notes.map((n) => n.lineId)).toEqual([2, 3])
  })

  it('splits rows between two waiters of the same kind', async () => {
    call.mockClear()
    const batch = createLineDecorationsBatch()
    const [first, second] = await Promise.all([batch.anchorsFor([1]), batch.anchorsFor([5])])
    expect(call).toHaveBeenCalledTimes(1)
    expect(first.map((a) => a.lineId)).toEqual([1])
    expect(second.map((a) => a.lineId)).toEqual([5])
  })
})
