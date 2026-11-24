/**
 * Which app window a component is drawn in.
 *
 * The page draws its main window and any extra app windows it opened (AppWindowLayer.vue,
 * the VS Code model: one page, many windows). Each window shows its own tabs; '' is the
 * main window. A component reads its window once in setup - it never moves to another
 * window (a tab moving between windows re-creates its page there).
 */
import { hasInjectionContext, inject, provide, type InjectionKey } from 'vue'

const APP_WINDOW_KEY: InjectionKey<string> = Symbol('appWindowKey')

/** Everything drawn below the caller belongs to app window `windowKey`. */
export function provideAppWindowKey(windowKey: string): void {
  provide(APP_WINDOW_KEY, windowKey)
}

/** The app window this component is drawn in ('' = the main window). Call during setup. */
export function useAppWindowKey(): string {
  return hasInjectionContext() ? inject(APP_WINDOW_KEY, '') : ''
}

/** The window name the page gives an extra app window; the host makes it a full window (AppViewerPopup.cs). */
export const APP_WINDOW_NAME_PREFIX = 'appwindow-'
