import { describe, expect, it, vi } from 'vitest'
import { effectScope, nextTick, ref } from 'vue'
import { useFastDocViewPictures } from './useFastDocViewPictures'

async function flush() {
  for (let i = 0; i < 5; i++) await Promise.resolve()
  await nextTick()
}

describe('useFastDocViewPictures', () => {
  it('withLoadedPictureSources resolves with the sources in place (the note tooltip, shown once)', async () => {
    let release: (value: string) => void = () => {}
    const scope = effectScope()
    const pictures = scope.run(() => useFastDocViewPictures(
      { pictureSource: () => new Promise<string>((resolve) => { release = resolve }) }, () => 'd1'))!
    const html = '<p>note<img data-word-picture="f:rId1"></p>'
    let result: string | null = null
    void pictures.withLoadedPictureSources(html).then((value) => { result = value })
    await flush()
    expect(result).toBeNull()                                       // waits for the picture's source
    release('/p/d1/f%3ArId1')
    await flush()
    await flush()
    expect(result).toContain('data-word-picture="f:rId1" src="/p/d1/f%3ArId1"')
    expect(await pictures.withLoadedPictureSources('<p>none</p>')).toBe('<p>none</p>')
    expect(await pictures.withLoadedPictureSources(html)).toContain('src="/p/d1/f%3ArId1"')   // known: at once
    scope.stop()
  })

  it('requests each picture once and puts its source on the img', async () => {
    const requested: string[] = []
    const documentId = ref<string | undefined>('d1')
    const scope = effectScope()
    const pictures = scope.run(() => useFastDocViewPictures(
      { pictureSource: async (id, pictureId) => { requested.push(`${id}/${pictureId}`); return `/p/${id}/${encodeURIComponent(pictureId)}` } },
      () => documentId.value))!
    const html = '<p>a<img class="word-picture" data-word-picture="p:Pictures/a&amp;b.png" width="10" height="5" loading="lazy"></p>'
    expect(pictures.withPictureSources(html)).toBe(html)          // not known yet: no src, the box keeps its size
    pictures.withPictureSources(html)                             // a second render does not ask again
    await flush()
    expect(requested).toEqual(['d1/p:Pictures/a&b.png'])
    expect(pictures.withPictureSources(html)).toContain('data-word-picture="p:Pictures/a&amp;b.png" src="/p/d1/p%3APictures%2Fa%26b.png"')
    expect(pictures.withPictureSources('<p>no pictures</p>')).toBe('<p>no pictures</p>')
    documentId.value = 'd2'                                       // another document: sources start over
    await flush()
    expect(pictures.sourceCount()).toBe(0)
    scope.stop()
  })

  it('keeps a failed picture as its empty box without asking again', async () => {
    let calls = 0
    const scope = effectScope()
    const pictures = scope.run(() => useFastDocViewPictures(
      { pictureSource: async () => { calls++; throw new Error('gone') } }, () => 'd1'))!
    const html = '<img data-word-picture="1:0">'
    pictures.withPictureSources(html)
    await flush()
    expect(pictures.withPictureSources(html)).toBe(html)
    await flush()
    expect(calls).toBe(1)
    scope.stop()
  })

  it('a missing picture keeps its box silently; a late answer for a changed document is revoked', async () => {
    const revoked: string[] = []
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.stubGlobal('URL', { createObjectURL: () => '', revokeObjectURL: (url: string) => revoked.push(url) })
    let release: (value: string) => void = () => {}
    const documentId = ref('d1')
    const scope = effectScope()
    const pictures = scope.run(() => useFastDocViewPictures(
      { pictureSource: (_id, pictureId) => pictureId === 'missing' ? Promise.resolve(null) : new Promise<string>((resolve) => { release = resolve }) },
      () => documentId.value))!
    pictures.withPictureSources('<img data-word-picture="missing">')
    await flush()
    expect(pictures.withPictureSources('<img data-word-picture="missing">')).toBe('<img data-word-picture="missing">')
    pictures.withPictureSources('<img data-word-picture="late">')
    documentId.value = 'd2'                                         // the document changed while the picture was read
    await flush()
    release('blob:old/1')
    await flush()
    expect(revoked).toEqual(['blob:old/1'])
    expect(pictures.sourceCount()).toBe(0)
    expect(warn).not.toHaveBeenCalled()
    scope.stop()
    warn.mockRestore()
    vi.unstubAllGlobals()
  })

  it('revokes object URLs when the page goes away', async () => {
    const revoked: string[] = []
    vi.stubGlobal('URL', { createObjectURL: () => '', revokeObjectURL: (url: string) => revoked.push(url) })
    const scope = effectScope()
    const pictures = scope.run(() => useFastDocViewPictures({ pictureSource: async () => 'blob:a/1' }, () => 'd1'))!
    pictures.withPictureSources('<img data-word-picture="1">')
    await flush()
    expect(pictures.sourceCount()).toBe(1)
    scope.stop()
    expect(revoked).toEqual(['blob:a/1'])
    vi.unstubAllGlobals()
  })
})
