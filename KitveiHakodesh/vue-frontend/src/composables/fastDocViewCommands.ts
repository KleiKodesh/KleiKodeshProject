/**
 * App-level shortcuts aimed at the FastDocView page of a pane (Ctrl+K table of contents, Ctrl+J
 * notes pane, Ctrl+F search, Ctrl+B toolbar) - the same keys the book view answers through its
 * store. The title-bar shortcut handler sends; the page of that pane listens.
 */
import { onBeforeUnmount } from 'vue'

export type FastDocViewCommand = 'toggleToc' | 'toggleNotes' | 'openSearch' | 'toggleToolbar'

const bus = new EventTarget()

export function sendFastDocViewCommand(paneId: 1 | 2, command: FastDocViewCommand) {
  bus.dispatchEvent(new CustomEvent('command', { detail: { paneId, command } }))
}

export function useFastDocViewCommands(paneId: 1 | 2, handlers: Record<FastDocViewCommand, () => void>) {
  const listener = (event: Event) => {
    const { paneId: target, command } = (event as CustomEvent<{ paneId: 1 | 2; command: FastDocViewCommand }>).detail
    if (target === paneId) handlers[command]()
  }
  bus.addEventListener('command', listener)
  onBeforeUnmount(() => bus.removeEventListener('command', listener))
}
