import { getCurrentScope, onScopeDispose } from 'vue'
import { onWebviewEvent } from './seforimDb'

/**
 * A press on the app's NATIVE chrome — the FluentChromeTabs strip, the title bar,
 * the caption buttons, the resize border.
 *
 * Those surfaces are separate Win32 windows owned by the host, not part of this
 * document, so pressing them dispatches no DOM event at all: `onClickOutside`
 * cannot see it, and the window `blur` fallback usually doesn't fire either
 * because the strip handles the mouse without durably taking focus. Any overlay
 * that closes on an outside click would otherwise stay open over the page while
 * the user is plainly interacting with the window frame.
 *
 * The host closes that gap by pushing a `chromePressed` event (AppViewerFocus.cs)
 * from its message filter, which this module fans out to subscribers. Presses
 * inside the WebView are filtered out on the C# side, so every event here really
 * is an "outside" press.
 *
 * No-op in the dev browser and the VSTO task pane, where there is no native
 * chrome and the event is never pushed.
 */
type Handler = () => void

const handlers = new Set<Handler>()

// One bus subscription for the whole app, no matter how many dropdowns are alive —
// they come and go constantly as the user opens and closes menus. Subscribing here
// rather than lazily on first use keeps it to exactly one listener per module
// instance, so a hot reload can't leave a second one behind holding a stale Set.
onWebviewEvent((msg) => {
  if (msg.event !== 'chromePressed') return
  // Copy first: a handler that closes a dropdown may unmount a component whose own
  // handler is still in the set, which would mutate it mid-iteration.
  for (const handler of [...handlers]) handler()
})

/**
 * Runs `fn` whenever the native chrome is pressed. Auto-unsubscribes with the
 * calling effect scope (component unmount); the returned stop function is for
 * callers outside a scope.
 */
export function onNativeChromePressed(fn: Handler): () => void {
  handlers.add(fn)
  const stop = () => handlers.delete(fn)
  if (getCurrentScope()) onScopeDispose(stop)
  return stop
}
