import { PDFDataRangeTransport } from 'pdfjs-dist'

/**
 * Random access to PDFs served by the host (WebView2): it answers a Range request with 206, but a
 * plain GET carries no Content-Length / Accept-Ranges, so PDF.js would read the whole file first.
 * A probe of the first part gives the size (Content-Range of the 206) and the first bytes; after
 * that only the parts PDF.js asks for are read - never the whole file (open with
 * disableAutoFetch). With the sidecar's page byte map the worker asks for a page's whole byte set
 * at once (pdfSidecarLink.ts).
 */
const PROBE_BYTES = 64 * 1024
/** PDF.js's rangeChunkSize for this path. */
/** Tries per part before giving up (network hiccups, a busy host). */
const RANGE_RETRIES = 4

export const RANGE_CHUNK_BYTES = 64 * 1024

export class PdfRangeTransport extends PDFDataRangeTransport {
  private readonly url: string
  private readonly aborter = new AbortController()

  constructor(url: string, length: number, initialData: Uint8Array) {
    super(length, initialData)
    this.url = url
  }

  /** File size in bytes (from the probe's Content-Range). */
  get size(): number {
    return this.length
  }

  /** Bytes [begin, end) of the file, read straight from the host (not through PDF.js). */
  async readRange(begin: number, end: number): Promise<Uint8Array> {
    const r = await fetch(this.url, { headers: { Range: `bytes=${begin}-${end - 1}` }, signal: this.aborter.signal })
    if (r.status !== 206) throw new Error(`Range request answered ${r.status}`)
    return new Uint8Array(await r.arrayBuffer())
  }

  override requestDataRange(begin: number, end: number) {
    void this.deliverRange(begin, end)
  }

  // A failed part is tried again (the transport has no way to report an error to PDF.js: a part
  // that never arrives leaves its page waiting for ever). After the last try it is logged.
  private async deliverRange(begin: number, end: number) {
    for (let attempt = 1; ; attempt++) {
      try {
        this.onDataRange(begin, await this.readRange(begin, end))
        return
      } catch (e) {
        if (this.aborter.signal.aborted) return
        if (attempt >= RANGE_RETRIES) {
          console.error('PDF part fetch failed', e)
          return
        }
        await new Promise((resolve) => setTimeout(resolve, 250 * attempt))
      }
    }
  }

  override abort() {
    this.aborter.abort()
  }
}

/** A range transport for `url` when the host answers Range with 206; otherwise null (open by url). */
export async function rangeTransportFor(url: string): Promise<PdfRangeTransport | null> {
  try {
    const r = await fetch(url, { headers: { Range: `bytes=0-${PROBE_BYTES - 1}` } })
    const size = Number(/\/(\d+)\s*$/.exec(r.headers.get('Content-Range') ?? '')?.[1])
    if (r.status !== 206 || !(size > 0)) {
      void r.body?.cancel()
      return null
    }
    return new PdfRangeTransport(url, size, new Uint8Array(await r.arrayBuffer()))
  } catch {
    return null
  }
}
