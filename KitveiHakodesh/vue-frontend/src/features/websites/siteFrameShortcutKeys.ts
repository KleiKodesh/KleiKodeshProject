/**
 * Which keys pressed inside a Torah website's frame belong to the APP rather than the site.
 *
 * App navigation wins: F1-F12, Alt+Left/Right (Back/Forward), Alt+W/X (history), and Ctrl with a
 * letter, digit, Tab, PageUp/PageDown, +, - or \. Text editing, find and print stay with the site:
 * Ctrl+A/C/V/X/Z/Y/P, plain Ctrl+F (the app's find cannot search inside another site's page),
 * arrows, Backspace... Ctrl+Alt together is AltGr (typing a character such as the shekel sign on a
 * Hebrew keyboard), so it always stays with the site.
 *
 * The host applies the same rule in AppViewerSiteShortcuts.cs (IsAppShortcut); keep them in step.
 * This copy serves the dev browser, where the dev proxy injects a forwarder into site pages.
 */
export function isAppShortcutKey(code: string, ctrl: boolean, alt: boolean, shift = false): boolean {
  if (/^F\d{1,2}$/.test(code)) return true
  if (ctrl && alt) return false
  if (alt) return ['ArrowLeft', 'ArrowRight', 'KeyW', 'KeyX'].includes(code)
  if (!ctrl) return false
  if (code === 'KeyF') return shift // Ctrl+Shift+F = app fullscreen; Ctrl+F = the site's find
  if (['KeyA', 'KeyC', 'KeyV', 'KeyX', 'KeyZ', 'KeyY', 'KeyP'].includes(code)) return false
  return /^(Key[A-Z]|Digit\d)$/.test(code) || ['Tab', 'PageUp', 'PageDown', 'Equal', 'Minus', 'Backslash'].includes(code)
}

/**
 * The dev forwarder injected into proxied site pages (websitesDevProxy.ts): posts app shortcut
 * keys to the app window and keeps them from the site. Self-contained source - it runs in the
 * site's page, so it cannot import anything.
 */
export const SITE_FRAME_KEY_FORWARDER = `(function () {
  if (window === window.top) return;
  ${isAppShortcutKey.toString().replace(/^function \w+/, 'function isApp')}
  window.addEventListener('keydown', function (e) {
    if (!isApp(e.code, e.ctrlKey || e.metaKey, e.altKey, e.shiftKey)) return;
    e.preventDefault();
    e.stopImmediatePropagation();
    window.top.postMessage({ type: 'iframeKeydown', code: e.code, key: e.key, repeat: e.repeat, ctrlKey: e.ctrlKey, shiftKey: e.shiftKey, metaKey: e.metaKey, altKey: e.altKey }, '*');
  }, true);
})();`
