/**
 * Opens an empty popup window that this page renders into.
 *
 * The popup is `about:blank`, so it has no script of its own: it shares this page's
 * script, stores and backend connection. The host turns a sized `window.open` into a
 * real window and remembers where the user left it, by `name` (AppViewerPopup.cs).
 */
import type { HostWindow } from './hostWindow'
import { mirrorPageStyles } from './mirrorPageStyles'

export interface DetachedWindowOptions {
  /** Stable name: the host remembers the window's size and place under it. */
  name: string
  title: string
  width: number
  height: number
}

export interface DetachedWindowHandle {
  window: HostWindow
  /** The element to render into: fills the popup. */
  mountElement: HTMLElement
  setTitle: (title: string) => void
  /** Closes the popup. `onClosedByUser` does not run for this. */
  close: () => void
}

/**
 * Returns null when the popup could not open (a browser's popup blocker).
 * `onClosedByUser` runs when the user closes the popup themselves.
 */
export function openDetachedWindow(
  options: DetachedWindowOptions,
  onClosedByUser: () => void,
): DetachedWindowHandle | null {
  const popup = window.open(
    'about:blank',
    options.name,
    `popup=yes,width=${options.width},height=${options.height}`,
  ) as HostWindow | null
  if (!popup) return null

  const popupDocument = popup.document
  // A reused name hands back the same popup with whatever it held before.
  popupDocument.head.replaceChildren()
  popupDocument.body.replaceChildren()
  popupDocument.title = options.title

  const stopMirroring = mirrorPageStyles(document, popupDocument)

  // Created by the popup's own document so it is a plain element there.
  const mountElement = popupDocument.createElement('div')
  mountElement.style.cssText =
    'position:fixed;inset:0;display:flex;flex-direction:column;background:var(--bg-primary);color:var(--text-primary)'
  popupDocument.body.appendChild(mountElement)

  let closedByUs = false

  function close() {
    if (closedByUs) return
    closedByUs = true
    popup!.close()
  }

  // The popup cannot outlive the page that renders into it.
  window.addEventListener('pagehide', close)

  popup.addEventListener(
    'pagehide',
    () => {
      stopMirroring()
      window.removeEventListener('pagehide', close)
      if (!closedByUs) onClosedByUser()
    },
    { once: true },
  )

  return {
    window: popup,
    mountElement,
    setTitle: (title) => {
      popupDocument.title = title
    },
    close,
  }
}
