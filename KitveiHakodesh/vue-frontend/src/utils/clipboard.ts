/**
 * Text-to-clipboard that also works in the hosted app.
 *
 * The host serves the app from http://KitveiHakodesh-vue-app/ — plain http on a
 * non-localhost hostname, which Chromium treats as an INSECURE context, so
 * `navigator.clipboard` is not exposed there at all and reaching for `.writeText`
 * throws outright. Dev is served from http://localhost, a secure context where the
 * async API works — which is exactly why a clipboard call written against it looks
 * fine in dev and silently does nothing in the demo app and the add-in.
 *
 * `execCommand('copy')` over an off-screen textarea is the one path both contexts
 * share; it is the same primitive every other copy path in the app already goes
 * through (useLineCopy, GlobalContextMenu).
 */
export async function copyTextToClipboard(text: string): Promise<boolean> {
  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text)
      return true
    } catch {
      // Permission denied or a non-focused document — the fallback below still works.
    }
  }
  return copyViaTextarea(text)
}

/**
 * How long a pre-copy await may take before the copy goes ahead without it.
 *
 * `execCommand('copy')` only works while the browser still considers the document to have
 * transient user activation, which Chromium revokes about a second after the gesture. An
 * await that outlasts it turns the copy into a silent no-op — and the Ctrl+C keydown was
 * already preventDefault()ed, so there is no native copy to fall back on either.
 */
const ACTIVATION_BUDGET_MS = 350

/**
 * Awaits `work`, but never for longer than the clipboard's activation budget: past that the
 * copy must proceed regardless, because losing a decoration (an endnote, a citation) is a far
 * smaller failure than the copy doing nothing at all. Never rejects.
 *
 * `work` keeps running in the background, so whatever it was loading is in place for the
 * next copy.
 */
export function awaitWithinActivationWindow(work: Promise<unknown>): Promise<void> {
  return new Promise<void>((resolve) => {
    const timer = setTimeout(resolve, ACTIVATION_BUDGET_MS)
    const done = () => {
      clearTimeout(timer)
      resolve()
    }
    work.then(done, done)
  })
}

function copyViaTextarea(text: string): boolean {
  // Off-screen rather than hidden: `display: none` / `visibility: hidden` elements
  // cannot hold a selection, so the copy would come up empty.
  const textarea = document.createElement('textarea')
  textarea.value = text
  textarea.setAttribute('readonly', '')
  textarea.style.position = 'fixed'
  textarea.style.top = '-9999px'
  textarea.style.opacity = '0'
  document.body.appendChild(textarea)

  const previouslyFocused = document.activeElement as HTMLElement | null
  try {
    textarea.select()
    // The copy event bubbles to document, where GlobalContextMenu's clean-text
    // listener sits — it bails on textarea targets, so the payload stays verbatim.
    return document.execCommand('copy')
  } catch {
    return false
  } finally {
    textarea.remove()
    previouslyFocused?.focus?.()
  }
}
