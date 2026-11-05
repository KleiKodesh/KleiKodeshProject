/**
 * The FastDocView service ops: a Word-family / text document read in windows of blocks,
 * never whole. The contract is documented in features/fast-doc-view/README.md - the C#
 * side (Core FastDocView + one Dispatcher case per op) must match it exactly.
 */
import { serviceCall, serviceGetUrl } from './serviceClient'

/** What `fastDocViewOpen` returns: the document's identity for every later call, and its shape. */
export interface FastDocViewSummary {
  documentId: string
  /** The scroller's row count: the blocks known so far. While the index is still being
   *  built (first open of a big file) it only grows - it is never an estimate. */
  blockCount: number
  /** Blocks the index has found so far (= blockCount). */
  knownBlockCount: number
  isIndexComplete: boolean
  isRightToLeft: boolean
  /** CSS for the rendered blocks; scoped under the page's content root before use. */
  stylesheet: string
  headingCount: number
  footnoteCount: number
  endnoteCount: number
  /** The title the file declares itself (a Torat Emet book name); absent when it has none. */
  title?: string | null
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
  /** Drawn before the index was final with something only the final index knows (an .odt connector
   * glued to shapes in other blocks): fetched again once the index is complete. */
  provisional?: boolean
}

/** What `fastDocViewScan` returns: the index state after one more building step. */
export interface FastDocViewScanState {
  knownBlockCount: number
  isIndexComplete: boolean
  /** Note counts grow with the index, like the block count. */
  footnoteCount: number
  endnoteCount: number
}

export interface FastDocViewHeading {
  blockIndex: number
  level: number
  text: string
}

/**
 * One bounded search step: hit blocks of the scanned range (ascending) with, parallel to them, how
 * many times the query occurs in each; where to continue (-1 = end).
 */
export interface FastDocViewSearchStep {
  hitBlocks: number[]
  hitCounts: number[]
  nextBlock: number
  reachedEnd: boolean
}

/** A run of blocks at one page text width (a Word section); twips = 1/1440 inch. */
export interface FastDocViewSection {
  firstBlockIndex: number
  textWidthTwips: number
}

/** Everything the page needs from the service - injectable so the composable is testable. */
export interface FastDocViewApi {
  /** `openId`: the page's id for this open, so `cancelOpen` can stop it while it runs. */
  open(path: string, openId?: string): Promise<FastDocViewSummary>
  /**
   * Stops the service's work on a running open (the tab closed, or another file opened before it returned);
   * the open then fails with "Cancelled." and nothing stays open. Optional: a host without it lets the open finish.
   */
  cancelOpen?(openId: string): Promise<void>
  blocks(documentId: string, first: number, count: number): Promise<FastDocViewBlock[]>
  headings(documentId: string): Promise<FastDocViewHeading[]>
  /** Notes [first, first + count) in body order (the notes pane's own windows over every note); at most 100. */
  notes(documentId: string, first: number, count: number): Promise<FastDocViewNote[]>
  /** One Int32 per known note (body order): the block holding its reference. Grows with the index. */
  noteBlocks(documentId: string): Promise<Int32Array>
  /** Searches up to `maxBlocks` blocks from `fromBlock` (block indexes + match counts only; book-view normalisation). */
  search(documentId: string, query: string, matchCase: boolean, fromBlock: number, maxBlocks: number): Promise<FastDocViewSearchStep>
  /** The block holding bookmark `name` (an internal link's target), or null when there is none. */
  bookmark(documentId: string, name: string): Promise<number | null>
  /** Page text width per section, in block order (the known ones while the index builds). */
  sections(documentId: string): Promise<FastDocViewSection[]>
  /** One weight (estimated text length) per block. */
  weights(documentId: string): Promise<Int32Array>
  /** Builds `blocks` more of the index (renders nothing) and returns the new state. */
  scan(documentId: string, blocks: number): Promise<FastDocViewScanState>
  /**
   * An <img> source for picture `pictureId` (a block's `data-word-picture`): a URL the browser
   * fetches when the image comes into view (dev: the service's GET endpoint), or an object URL
   * of the bytes read through the service (hosted). One picture per call - pictures never travel
   * inside the block HTML. Object URLs (`blob:`) are the caller's to revoke. Null when the
   * document has no such picture, or is no longer open (hosted: the service answers empty).
   */
  pictureSource(documentId: string, pictureId: string): Promise<string | null>
  close(documentId: string): Promise<void>
}

/** What `fastDocViewPicture` returns: one picture's bytes. */
interface FastDocViewPictureReply {
  contentType: string
  bytes: Uint8Array
}

/** The service's GET path for one picture (HttpHostServer: the open document id is the capability). */
export function fastDocViewPicturePath(documentId: string, pictureId: string): string {
  return `/fast-doc-view-picture/${encodeURIComponent(documentId)}/${encodeURIComponent(pictureId)}`
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

/**
 * The service's error text when the library could not open the file:
 * "FastDocViewOpenFailed:<code>: detail" (Core FastDocViewOpenFailedException). The page shows
 * its own translated message for the code; the detail stays as a secondary line.
 */
export const FAST_DOC_VIEW_OPEN_FAILED = 'FastDocViewOpenFailed'
export const FAST_DOC_VIEW_OPEN_FAILURE_CODES = ['encrypted', 'corrupt', 'online-only', 'not-found', 'unsupported', 'access-denied'] as const
export type FastDocViewOpenFailureCode = (typeof FAST_DOC_VIEW_OPEN_FAILURE_CODES)[number]

/** The open-failure code and the raw detail, or null when the error carries no known code. */
export function parseFastDocViewOpenFailure(error: unknown): { code: FastDocViewOpenFailureCode; detail: string } | null {
  const message = error instanceof Error ? error.message : String(error)
  const match = /FastDocViewOpenFailed:([a-z-]+):\s*([\s\S]*)/.exec(message)
  if (!match) return null
  const code = match[1] as FastDocViewOpenFailureCode
  if (!FAST_DOC_VIEW_OPEN_FAILURE_CODES.includes(code)) return null
  return { code, detail: match[2]!.trim() }
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
  open: (path, openId) => serviceCall<FastDocViewSummary>('fastDocViewOpen', { path, openId }),
  cancelOpen: async (openId) => {
    await serviceCall('fastDocViewCancelOpen', { openId })
  },
  blocks: async (documentId, first, count) =>
    (await serviceCall<FastDocViewBlock[]>('fastDocViewBlocks', { documentId, first, count })) ?? [],
  notes: async (documentId, first, count) =>
    (await serviceCall<FastDocViewNote[]>('fastDocViewNotes', { documentId, first, count })) ?? [],
  noteBlocks: async (documentId) =>
    decodeFastDocViewWeights(await serviceCall<Uint8Array>('fastDocViewNoteBlocks', { documentId })),
  headings: async (documentId) =>
    (await serviceCall<FastDocViewHeading[]>('fastDocViewHeadings', { documentId })) ?? [],
  search: async (documentId, query, matchCase, fromBlock, maxBlocks) =>
    (await serviceCall<FastDocViewSearchStep>('fastDocViewSearch', { documentId, query, matchCase, fromBlock, maxBlocks })) ??
    { hitBlocks: [], hitCounts: [], nextBlock: -1, reachedEnd: true },
  bookmark: async (documentId, name) =>
    (await serviceCall<{ blockIndex?: number | null }>('fastDocViewBookmark', { documentId, name }))?.blockIndex ?? null,
  sections: async (documentId) =>
    (await serviceCall<FastDocViewSection[]>('fastDocViewSections', { documentId })) ?? [],
  weights: async (documentId) =>
    decodeFastDocViewWeights(await serviceCall<Uint8Array>('fastDocViewWeights', { documentId })),
  scan: (documentId, blocks) => serviceCall<FastDocViewScanState>('fastDocViewScan', { documentId, blocks }),
  pictureSource: async (documentId, pictureId) => {
    const url = await serviceGetUrl(fastDocViewPicturePath(documentId, pictureId))
    if (url) return url
    const reply = await serviceCall<FastDocViewPictureReply | undefined>('fastDocViewPicture', { documentId, pictureId })
    if (!reply?.bytes?.length) return null
    return URL.createObjectURL(new Blob([reply.bytes as BlobPart], { type: reply.contentType }))
  },
  close: async (documentId) => {
    await serviceCall('fastDocViewClose', { documentId })
  },
}
