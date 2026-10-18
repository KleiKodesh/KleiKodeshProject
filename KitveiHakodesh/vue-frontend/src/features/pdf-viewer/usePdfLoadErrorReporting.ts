/**
 * Reports every PDF load failure in full to the console.
 *
 * PDF.js's own message ("An error occurred while loading the PDF.") names neither the URL nor
 * the HTTP status nor what the server said. On `documenterror` this logs the file URL and
 * PDF.js's reason, then re-requests the first bytes of the file to log the status, content
 * type and — when it is not a PDF — the response body (the service/host error text).
 * Script errors and unhandled rejections inside the viewer iframe are logged too.
 */
interface PdfJsApp {
  initializedPromise?: Promise<void>
  eventBus?: { on(name: string, fn: (e: { message?: string; reason?: string | null }) => void): void }
  url?: string
  pdfLoadingTask?: { promise: Promise<unknown> } | null
}

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

export function attachPdfLoadErrorReporting(win: Window, fileUrl: string | null): void {
  win.addEventListener('error', (e) => {
    console.error('[pdf-load] viewer script error', e.message, e.filename, e.lineno, e.error)
  })
  win.addEventListener('unhandledrejection', (e) => {
    console.error('[pdf-load] viewer unhandled rejection', e.reason)
  })

  const app = (win as Window & { PDFViewerApplication?: PdfJsApp }).PDFViewerApplication
  if (!app?.initializedPromise) {
    console.error('[pdf-load] PDFViewerApplication not found in the viewer iframe', { fileUrl })
    return
  }
  let reported = false
  const report = (detail: unknown) => {
    if (reported) return
    reported = true
    const url = app.url || fileUrl || ''
    console.error('[pdf-load] PDF failed to load', { url }, detail)
    if (url) void probeFile(url)
  }
  app.initializedPromise.then(() => {
    // The iframe's load event can come AFTER a fast failure (e.g. a 404), so also catch the
    // loading task already in flight; documenterror covers later failures (pages, reopen).
    app.pdfLoadingTask?.promise.catch(report)
    app.eventBus?.on('documenterror', (e) => report({ message: e.message, reason: e.reason }))
  }, (err) => console.error('[pdf-load] PDF.js viewer failed to initialize', err))
}
