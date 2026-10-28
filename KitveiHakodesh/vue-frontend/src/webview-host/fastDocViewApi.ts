/**
 * The FastDocView service ops: a Word-family / text document read in windows of blocks,
 * never whole. The contract is documented in features/fast-doc-view/README.md - the C#
 * side (Core FastDocView + one Dispatcher case per op) must match it exactly.
 */
import { serviceCall } from './serviceClient'

/** What `fastDocViewOpen` returns: the document's identity for every later call, and its shape. */
export interface FastDocViewSummary {
  documentId: string
  /** The scroller's row count. While the index is incomplete this is an ESTIMATE. */
  blockCount: number
  /** Blocks the index has actually found so far (= blockCount once complete). */
  knownBlockCount: number
  isIndexComplete: boolean
  isRightToLeft: boolean
  /** CSS for the rendered blocks; scoped under the page's content root before use. */
  stylesheet: string
  headingCount: number
  footnoteCount: number
  endnoteCount: number
}

export interface FastDocViewNote {
  kind: 'footnote' | 'endnote'
  number: number
  /** The mark as the document shows it (a custom mark, or the number in its format). */
  label: string
  html: string
}

export interface FastDocViewBlock {
  index: number
  kind: string
  html: string
  notes: FastDocViewNote[]
}

export interface FastDocViewHeading {
  blockIndex: number
  level: number
  text: string
}

/** Everything the page needs from the service - injectable so the composable is testable. */
export interface FastDocViewApi {
  open(path: string): Promise<FastDocViewSummary>
  blocks(documentId: string, first: number, count: number): Promise<FastDocViewBlock[]>
  headings(documentId: string): Promise<FastDocViewHeading[]>
  /** One weight (estimated text length) per block. */
  weights(documentId: string): Promise<Int32Array>
  close(documentId: string): Promise<void>
}

/**
 * The service's error text when the file changed on disk since it was opened (or the
 * document id is no longer open): the page reopens and asks again.
 */
export const FAST_DOC_VIEW_SOURCE_CHANGED = 'FastDocViewSourceChanged'

export function isFastDocViewSourceChangedError(error: unknown): boolean {
  const message = error instanceof Error ? error.message : String(error)
  return message.includes(FAST_DOC_VIEW_SOURCE_CHANGED)
}

/** Little-endian Int32 per block, from the raw bytes the service sends. */
export function decodeFastDocViewWeights(bytes: Uint8Array | ArrayLike<number> | null | undefined): Int32Array {
  if (!bytes) return new Int32Array(0)
  const array = bytes instanceof Uint8Array ? bytes : Uint8Array.from(bytes)
  const count = Math.floor(array.byteLength / 4)
  const view = new DataView(array.buffer, array.byteOffset, count * 4)
  const weights = new Int32Array(count)
  for (let index = 0; index < count; index++) weights[index] = view.getInt32(index * 4, true)
  return weights
}

export const fastDocViewApi: FastDocViewApi = {
  open: (path) => serviceCall<FastDocViewSummary>('fastDocViewOpen', { path }),
  blocks: async (documentId, first, count) =>
    (await serviceCall<FastDocViewBlock[]>('fastDocViewBlocks', { documentId, first, count })) ?? [],
  headings: async (documentId) =>
    (await serviceCall<FastDocViewHeading[]>('fastDocViewHeadings', { documentId })) ?? [],
  weights: async (documentId) =>
    decodeFastDocViewWeights(await serviceCall<Uint8Array>('fastDocViewWeights', { documentId })),
  close: async (documentId) => {
    await serviceCall('fastDocViewClose', { documentId })
  },
}
