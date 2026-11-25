import { InvalidPDFException, PasswordException, ResponseException } from 'pdfjs-dist'

/** Why a document did not open, as the pane shows it (pdfReader.load.*). */
export interface PdfLoadFailure {
  kind: 'invalid' | 'missing' | 'response' | 'password' | 'unknown'
  status?: number
  message: string
}

/** PDF.js's load errors, sorted as PDF.js's own app.js sorts them, plus a cancelled password. */
export function classifyPdfLoadError(err: unknown, passwordCancelled: boolean): PdfLoadFailure {
  const message = err instanceof Error ? err.message : String(err)
  if (passwordCancelled || err instanceof PasswordException) return { kind: 'password', message }
  if (err instanceof InvalidPDFException) return { kind: 'invalid', message }
  if (err instanceof ResponseException) {
    return err.missing || err.status === 404 ? { kind: 'missing', status: err.status, message } : { kind: 'response', status: err.status, message }
  }
  return { kind: 'unknown', message }
}

/** The first bytes of the file: status, type and - when it is not a PDF - what the server said. */
async function probeFile(url: string): Promise<void> {
  try {
    const res = await fetch(url, { headers: { Range: 'bytes=0-1023' }, cache: 'no-store' })
    const type = res.headers.get('Content-Type') ?? ''
    const bytes = new Uint8Array(await res.arrayBuffer())
    const head = String.fromCharCode(...bytes.slice(0, 5))
    const info = { status: res.status, statusText: res.statusText, contentType: type, bytes: bytes.length, startsWithPdf: head === '%PDF-' }
    if (head === '%PDF-') console.error('[pdf-load] file probe: server returns a PDF', url, info)
    else console.error('[pdf-load] file probe: NOT a PDF', url, info, '\n' + new TextDecoder().decode(bytes))
  } catch (err) {
    console.error('[pdf-load] file probe: request failed', url, err)
  }
}

/**
 * Reports a load failure in full to the console, as the old viewer's usePdfLoadErrorReporting.ts:
 * PDF.js's reason, the URL, and a probe of the file. A cancelled password is not a failure.
 */
export function reportPdfLoadFailure(url: string | null, err: unknown, failure: PdfLoadFailure) {
  if (failure.kind === 'password') return
  console.error('[pdf-load] PDF failed to load', { url, kind: failure.kind, status: failure.status }, err)
  if (url) void probeFile(url)
}
