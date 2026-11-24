/**
 * App-level shortcuts aimed at the FastDocView page on screen (Ctrl+K table of contents, Ctrl+J
 * notes pane, Ctrl+F search, Ctrl+B toolbar) - the same keys the book view answers through its
 * store. The title-bar shortcut handler sends; the page listens.
 */
import { onBeforeUnmount } from 'vue'

export type FastDocViewCommand = 'toggleToc' | 'toggleNotes' | 'openSearch' | 'toggleToolbar'

const bus = new EventTarget()

export function sendFastDocViewCommand(command: FastDocViewCommand) {
  bus.dispatchEvent(new CustomEvent('command', { detail: command }))
}

export function useFastDocViewCommands(handlers: Record<FastDocViewCommand, () => void>) {
  const listener = (event: Event) => handlers[(event as CustomEvent<FastDocViewCommand>).detail]()
  bus.addEventListener('command', listener)
  onBeforeUnmount(() => bus.removeEventListener('command', listener))
}
