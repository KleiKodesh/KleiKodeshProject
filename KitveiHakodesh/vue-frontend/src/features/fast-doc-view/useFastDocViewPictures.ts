/**
 * Pictures of the FastDocView page. The library's block HTML carries a picture as
 * `<img data-word-picture="ID" width height loading="lazy" decoding="async">` without a source:
 * the bytes never ride in the block window. This composable gives each such image its `src`
 * (the api's `pictureSource`: the service's GET URL in dev, an object URL in the hosted app),
 * one picture per request and only for the blocks being shown. Until the source is known the
 * image keeps its width / height, so the row does not jump; the block re-renders once it is.
 *
 * Sources are kept per open document in a bounded map (oldest out first); object URLs are
 * revoked when they leave it, when the document changes and when the page goes away.
 */
import { onScopeDispose, shallowRef, watch } from 'vue'
import type { FastDocViewApi } from '@/webview-host/fastDocViewApi'

/** The attribute the library writes (WordPictureHtml.PictureIdAttribute). */
export const FAST_DOC_VIEW_PICTURE_ATTRIBUTE = 'data-word-picture'
/** Sources kept at most: far more than one screen of pictures, small next to the window cache. */
const MAX_PICTURE_SOURCES = 256

const PICTURE_ATTRIBUTE_PATTERN = /data-word-picture="([^"]*)"/g

function decodeAttribute(value: string): string {
  return value.replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&')
}

function encodeAttribute(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;')
}

export function useFastDocViewPictures(api: Pick<FastDocViewApi, 'pictureSource'>, documentId: () => string | undefined) {
  /** pictureId -> source, for the current document; replaced (not mutated) so renders that read it re-run. */
  const sources = shallowRef(new Map<string, string>())
  const pending = new Set<string>()
  const failed = new Set<string>()
  let sourcesDocumentId: string | undefined

  function revoke(source: string) {
    if (source.startsWith('blob:')) URL.revokeObjectURL(source)
  }

  function clear() {
    for (const source of sources.value.values()) revoke(source)
    sources.value = new Map()
    pending.clear()
    failed.clear()
  }

  watch(documentId, (id) => {
    if (id === sourcesDocumentId) return
    sourcesDocumentId = id
    clear()
  }, { immediate: true })
  onScopeDispose(clear)

  async function load(id: string, pictureId: string) {
    pending.add(pictureId)
    try {
      const source = await api.pictureSource(id, pictureId)
      if (documentId() !== id) { if (source) revoke(source); return }
      // no such picture (or the document closed meanwhile): the sized empty box stays, silently
      if (!source) { failed.add(pictureId); return }
      const next = new Map(sources.value)
      next.set(pictureId, source)
      while (next.size > MAX_PICTURE_SOURCES) {
        const [oldestId, oldest] = next.entries().next().value as [string, string]
        next.delete(oldestId)
        revoke(oldest)
      }
      sources.value = next
    } catch (error) {
      // a picture the service cannot give (gone, a format it does not serve) keeps its empty box
      // (the service call already printed its failure; one line per document, not per picture)
      if (documentId() !== id) return
      if (failed.size === 0) console.warn(`[fast-doc-view] picture ${pictureId}`, error)
      failed.add(pictureId)
    } finally {
      pending.delete(pictureId)
    }
  }

  /** The block (or note) HTML with a `src` on every picture whose source is known; the others are requested. */
  function withPictureSources(html: string): string {
    const id = documentId()
    if (!id || !html.includes(FAST_DOC_VIEW_PICTURE_ATTRIBUTE)) return html
    const known = sources.value
    return html.replace(PICTURE_ATTRIBUTE_PATTERN, (attribute, encoded: string) => {
      const pictureId = decodeAttribute(encoded)
      const source = known.get(pictureId)
      if (source) return `${attribute} src="${encodeAttribute(source)}"`
      if (!pending.has(pictureId) && !failed.has(pictureId)) void load(id, pictureId)
      return attribute
    })
  }

  return { withPictureSources, sourceCount: () => sources.value.size }
}
