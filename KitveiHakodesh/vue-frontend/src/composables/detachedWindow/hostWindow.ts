/**
 * The browser window a component is rendered in.
 *
 * Normally that is the app's own window. A component rendered inside a detached
 * window (see DetachedWindow.vue) lives in a popup that shares this page's script
 * but has its own `window` and `document`. The globals `window` / `document` always
 * name the MAIN window, so anything rendered there that reads the selection, listens
 * on the document, measures the viewport, observes sizes or teleports to the body
 * must use the window from here instead.
 *
 * A component is created for one window and never moves to another (detaching
 * re-creates it), so reading this once in setup is enough.
 */
import { hasInjectionContext, inject, provide, type InjectionKey } from 'vue'

export type HostWindow = Window & typeof globalThis

const HOST_WINDOW: InjectionKey<HostWindow> = Symbol('hostWindow')

/** Everything rendered below the caller lives in `hostWindow`. */
export function provideHostWindow(hostWindow: HostWindow): void {
  provide(HOST_WINDOW, hostWindow)
}

/** The window this component is rendered in. Call during setup. */
export function useHostWindow(): HostWindow {
  return hasInjectionContext() ? inject(HOST_WINDOW, window) : window
}

/** The detached windows open now; app-wide UI (the toast) shows in the one in use. */
const openDetachedWindows = new Set<HostWindow>()

export function registerDetachedWindow(detached: HostWindow): () => void {
  openDetachedWindows.add(detached)
  return () => openDetachedWindows.delete(detached)
}

/** The window the reader is working in: a detached one that has focus, else the app's own. */
export function activeHostWindow(): HostWindow {
  for (const detached of openDetachedWindows) {
    if (!detached.closed && detached.document.hasFocus()) return detached
  }
  return window
}

/** True inside a detached window. Call during setup. */
export function useIsInDetachedWindow(): boolean {
  return useHostWindow() !== window
}
