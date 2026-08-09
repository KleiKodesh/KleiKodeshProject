import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { triggerCopy, attachScopedCopy } from './useLineCopy'

/**
 * Guards the cross-document copy contract behind the PDF viewer's copy menu.
 *
 * PDF.js renders in a same-origin iframe, so the user's selection lives in the IFRAME
 * document while our context menu lives in the parent. execCommand('copy') acts on the
 * focused document, so a copy issued against the parent silently no-ops. That produced
 * two user-visible bugs: "העתק" did nothing, and "העתק לתוך וורד" pasted whatever the
 * user had copied EARLIER — because the bridge call fired unconditionally after the
 * failed copy.
 *
 * The fix mirrors book view: run the copy on the document that owns the selection, and
 * sequence the Word paste inside the copy event so it can't run without a fresh clipboard.
 */

/**
 * The suite runs on the 'node' environment (vitest.config.ts) since every other test
 * covers pure string logic, so there is no global `document`. These tests drive the
 * clipboard EVENT path, whose handler calls htmlToPlainText → document.createElement.
 * Rather than pull in jsdom for one file, install the minimal shim that path needs:
 * an element whose textContent is its innerHTML with tags stripped.
 */
const globalRef = globalThis as unknown as { document?: unknown }
const hadDocument = 'document' in globalRef
const originalDocument = globalRef.document

beforeEach(() => {
  globalRef.document = {
    createElement: () => ({
      innerHTML: '',
      get textContent() {
        return String(this.innerHTML).replace(/<[^>]*>/g, '')
      },
    }),
    execCommand: () => true,
  }
})

afterEach(() => {
  if (hadDocument) globalRef.document = originalDocument
  else delete globalRef.document
})

/** A stand-in for the PDF iframe's document, recording which document ran the command. */
function makeFakeDoc(succeeds: boolean, fireEvent: boolean) {
  const listeners: Array<(e: Event) => void> = []
  // The last copy event dispatched, exposed so tests can assert on preventDefault /
  // stopPropagation. Models the real event contract: preventDefault() flips
  // defaultPrevented, which is what gates the handler's stopPropagation call.
  let lastEvent: { defaultPrevented: boolean; stopPropagation: ReturnType<typeof vi.fn> } | null =
    null
  const doc = {
    execCommand: vi.fn((cmd: string) => {
      if (cmd !== 'copy') return false
      if (fireEvent) {
        const event = {
          clipboardData: { setData: vi.fn() },
          defaultPrevented: false,
          preventDefault: vi.fn(function (this: { defaultPrevented: boolean }) {
            this.defaultPrevented = true
          }),
          stopPropagation: vi.fn(),
        }
        lastEvent = event
        listeners.forEach((l) => l(event as unknown as Event))
      }
      return succeeds
    }),
    getLastEvent: () => lastEvent,
    addEventListener: vi.fn((type: string, fn: (e: Event) => void, _capture?: boolean) => {
      if (type === 'copy') listeners.push(fn)
    }),
    removeEventListener: vi.fn((type: string, fn: (e: Event) => void, _capture?: boolean) => {
      const i = listeners.indexOf(fn)
      if (i >= 0) listeners.splice(i, 1)
    }),
  }
  return doc as unknown as Document & {
    execCommand: ReturnType<typeof vi.fn>
    getLastEvent: () => { defaultPrevented: boolean; stopPropagation: ReturnType<typeof vi.fn> } | null
  }
}

describe('triggerCopy — target document', () => {
  /** The shim's execCommand, spied so we can assert the parent was left alone. */
  function parentExecCommand() {
    return (globalRef.document as { execCommand: ReturnType<typeof vi.fn> }).execCommand
  }

  beforeEach(() => {
    ;(globalRef.document as { execCommand: unknown }).execCommand = vi.fn(() => true)
  })

  it('runs the copy on the iframe document, never the parent', () => {
    const iframeDoc = makeFakeDoc(true, false)
    triggerCopy(undefined, iframeDoc)

    expect(iframeDoc.execCommand).toHaveBeenCalledWith('copy')
    // The original bug: the command went to the parent, where nothing was selected.
    expect(parentExecCommand()).not.toHaveBeenCalled()
  })

  it('defaults to the parent document so in-page callers are unchanged', () => {
    triggerCopy()
    expect(parentExecCommand()).toHaveBeenCalledWith('copy')
  })

  it('reports failure so callers can gate follow-up work', () => {
    expect(triggerCopy(undefined, makeFakeDoc(false, false))).toBe(false)
    expect(triggerCopy(undefined, makeFakeDoc(true, false))).toBe(true)
  })
})

describe('paste-into-Word sequencing', () => {
  it('runs afterCopy only via the copy event, after the clipboard is written', () => {
    const order: string[] = []
    const doc = makeFakeDoc(true, true)
    attachScopedCopy(doc, () => '<div dir="rtl">x</div>')

    triggerCopy(() => order.push('pasteIntoWord'), doc)

    expect(order).toEqual(['pasteIntoWord'])
  })

  it('never runs afterCopy when the copy event does not fire — the stale-clipboard bug', () => {
    const afterCopy = vi.fn()
    // execCommand returns true but no copy event reaches our handler (nothing selected
    // in that document). Word must NOT be told to paste: the clipboard still holds the
    // user's PREVIOUS copy, which is exactly what users reported being pasted.
    const doc = makeFakeDoc(true, false)
    attachScopedCopy(doc, () => '<div dir="rtl">x</div>')

    triggerCopy(afterCopy, doc)

    expect(afterCopy).not.toHaveBeenCalled()
  })

  it('does not leak an armed callback into a later unrelated copy', () => {
    const afterCopy = vi.fn()
    const silentDoc = makeFakeDoc(true, false)
    triggerCopy(afterCopy, silentDoc) // arms, but never fires

    // A subsequent plain copy elsewhere must not inherit the stale callback.
    const liveDoc = makeFakeDoc(true, true)
    attachScopedCopy(liveDoc, () => '<div dir="rtl">y</div>')
    triggerCopy(undefined, liveDoc)

    expect(afterCopy).not.toHaveBeenCalled()
  })
})

describe('attachScopedCopy', () => {
  it('binds to the document so PDF.js text-layer rebuilds cannot orphan it', () => {
    const doc = makeFakeDoc(true, true)
    const detach = attachScopedCopy(doc, () => '<div>x</div>')
    expect(doc.addEventListener).toHaveBeenCalledWith('copy', expect.any(Function), true)

    detach()
    expect(doc.removeEventListener).toHaveBeenCalledWith('copy', expect.any(Function), true)
  })

  it('listens in CAPTURE phase — PDF.js stopPropagation() never reaches bubble', () => {
    // Verified live: a bubble-phase listener on the PDF iframe document never fires, so
    // the formatted text/html flavor was silently never written and Word got PDF.js's
    // default plain-text payload instead.
    const doc = makeFakeDoc(true, true)
    attachScopedCopy(doc, () => '<div>x</div>')
    const useCapture = (doc.addEventListener as ReturnType<typeof vi.fn>).mock.calls[0]?.[2]
    expect(useCapture).toBe(true)
  })

  it('leaves the event alone when there is no selection to format', () => {
    const doc = makeFakeDoc(true, true)
    const build = vi.fn(() => null)
    attachScopedCopy(doc, build)

    triggerCopy(undefined, doc)
    expect(build).toHaveBeenCalled()
    // Unhandled event → PDF.js's own copy handler must still run (no stopPropagation).
    expect(doc.getLastEvent()?.stopPropagation).not.toHaveBeenCalled()
  })

  it('stops propagation after committing a payload — PDF.js must not overwrite text/plain', () => {
    // PDF.js's copy handler runs after ours (at the target) and re-writes text/plain
    // with its raw sel.toString() extraction — the merged-words payload on OCR'd text
    // layers. Once we've handled the event, it must not see it.
    const doc = makeFakeDoc(true, true)
    attachScopedCopy(doc, () => '<div dir="rtl">x y</div>')

    triggerCopy(undefined, doc)
    const event = doc.getLastEvent()
    expect(event?.defaultPrevented).toBe(true)
    expect(event?.stopPropagation).toHaveBeenCalled()
  })
})
