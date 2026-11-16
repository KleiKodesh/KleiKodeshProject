/**
 * A key combination as one string: `Ctrl+Alt+Shift+<KeyboardEvent.code>`, modifiers always
 * in that order, e.g. `Ctrl+KeyJ`, `Ctrl+Shift+KeyD`, `F8`, `Alt+ArrowRight`.
 *
 * Built on `e.code` (the physical key), never `e.key`: `e.key` is the character the key
 * types, which changes with the keyboard language - with Hebrew active, Ctrl+J arrives as
 * key "ח". Every shortcut in this app has always matched on `code` for that reason.
 *
 * Pure functions only - no Vue, no store - so the startup capture hook and the tests can use it.
 */

export type Combo = string

const MODIFIER_CODES = new Set([
  'ControlLeft', 'ControlRight', 'ShiftLeft', 'ShiftRight',
  'AltLeft', 'AltRight', 'MetaLeft', 'MetaRight', 'CapsLock', 'NumLock', 'ScrollLock',
])

/** The combination a keydown makes, or null for a lone modifier press (or an unknown key). */
export function comboFromEvent(e: Pick<KeyboardEvent, 'code' | 'ctrlKey' | 'altKey' | 'shiftKey' | 'metaKey'>): Combo | null {
  if (!e.code || MODIFIER_CODES.has(e.code)) return null
  return makeCombo(e.code, { ctrl: e.ctrlKey || e.metaKey, alt: e.altKey, shift: e.shiftKey })
}

export function makeCombo(code: string, mods: { ctrl?: boolean; alt?: boolean; shift?: boolean } = {}): Combo {
  return `${mods.ctrl ? 'Ctrl+' : ''}${mods.alt ? 'Alt+' : ''}${mods.shift ? 'Shift+' : ''}${code}`
}

export interface ParsedCombo {
  ctrl: boolean
  alt: boolean
  shift: boolean
  code: string
}

export function parseCombo(combo: Combo): ParsedCombo {
  const parts = combo.split('+')
  // The code is always the last part; '+' never occurs inside a code ('Equal', 'NumpadAdd').
  const code = parts[parts.length - 1] ?? ''
  return { ctrl: parts.includes('Ctrl'), alt: parts.includes('Alt'), shift: parts.includes('Shift'), code }
}

/** True for a string this module could have produced (used to drop junk read back from storage). */
export function isWellFormedCombo(value: unknown): value is Combo {
  if (typeof value !== 'string' || value.length === 0) return false
  const p = parseCombo(value)
  return makeCombo(p.code, p) === value && /^[A-Za-z0-9]+$/.test(p.code) && !MODIFIER_CODES.has(p.code)
}

const isFunctionKey = (code: string) => /^F([1-9]|1[0-2])$/.test(code)

/**
 * Why a combination may not be chosen as a shortcut, or null when it may.
 *
 * - `needsModifier`: without Ctrl or Alt a key types, scrolls or moves focus, so a shortcut
 *   on it would steal ordinary use. F1-F12 are the exception (with or without Shift) -
 *   this app already uses F1, F4, F8, F9 and F11 bare.
 * - `editing`: Ctrl+C/V/A/Z are copy, paste, select-all and undo in every text field.
 * - `system`: Windows acts on these before any program sees them (Alt+F4 closes the
 *   window); listed so the message is clear rather than the press simply vanishing.
 */
export type ComboProblem = 'needsModifier' | 'editing' | 'system'

const EDITING = new Set(['Ctrl+KeyC', 'Ctrl+KeyV', 'Ctrl+KeyA', 'Ctrl+KeyZ'])
const SYSTEM = new Set(['Alt+F4', 'Alt+Tab', 'Alt+Shift+Tab', 'Alt+Escape', 'Ctrl+Escape', 'Alt+Space', 'Ctrl+Shift+Escape'])

export function comboProblem(combo: Combo): ComboProblem | null {
  const p = parseCombo(combo)
  if (SYSTEM.has(combo)) return 'system'
  if (EDITING.has(combo)) return 'editing'
  if (!p.ctrl && !p.alt && !isFunctionKey(p.code)) return 'needsModifier'
  return null
}

/**
 * Combinations that never reach the page while the app runs inside Word's task pane.
 *
 * MEASURED 2026-11-16 (Word 16 / Microsoft 365, installed add-in, real keystrokes sent to a
 * focused Kitvei Hakodesh pane; a first-in-line capture listener on `window`, injected over
 * the WebView2 remote-debugging port, recorded every keydown the page got):
 *
 *   Never arrive: Ctrl+Shift+J, Ctrl+Shift+K, Ctrl+Shift+Q, Ctrl+Shift+W, F10, Ctrl+Shift+0.
 *   Arrive: every Ctrl+letter, every other Ctrl+Shift+letter, Ctrl+Alt+letter, Alt+letter,
 *   Alt+Shift+letter, Ctrl/Alt/Ctrl+Shift+digit, F1-F9, F11, F12, Shift/Ctrl/Alt+F-keys,
 *   Ctrl+Tab, Ctrl+Shift+Tab, Ctrl+Enter, Alt+arrows, Ctrl+arrows, Ctrl+-, Ctrl+=, Ctrl+\,
 *   Ctrl+Space.
 *
 * Not established: whether the six are taken by Word or by WebView2 itself (the standalone
 * app was not measured). Ctrl+Shift+J/W/Q are Chromium browser keys (DevTools console,
 * close window, quit), so WebView2 is the likely owner. Separately, another program's
 * system-wide hotkey takes its key before both (Ctrl+Alt+G was Google Drive's on the
 * measuring machine) - that differs per computer, which is why the settings grid captures
 * the real press: a combination that cannot arrive cannot be recorded in the first place.
 *
 * This list is for the defaults and for settings carried over from elsewhere: a row whose
 * combination is in it gets a warning when the app runs inside Word.
 */
export const UNREACHABLE_IN_WORD: ReadonlySet<Combo> = new Set([
  'Ctrl+Shift+KeyJ', 'Ctrl+Shift+KeyK', 'Ctrl+Shift+KeyQ', 'Ctrl+Shift+KeyW', 'F10', 'Ctrl+Shift+Digit0',
])

/** Visible label of a key code. `arrows` supplies the translated arrow names. */
export function keyLabel(code: string, arrows: { left: string; right: string; up: string; down: string }): string {
  if (code.startsWith('Key')) return code.slice(3)
  if (code.startsWith('Digit')) return code.slice(5)
  if (code.startsWith('Numpad')) return 'Num ' + code.slice(6)
  switch (code) {
    case 'ArrowLeft': return arrows.left
    case 'ArrowRight': return arrows.right
    case 'ArrowUp': return arrows.up
    case 'ArrowDown': return arrows.down
    case 'Escape': return 'Esc'
    case 'Backslash': return '\\'
    case 'Minus': return '-'
    case 'Equal': return '+'
    case 'BracketLeft': return '['
    case 'BracketRight': return ']'
    case 'Semicolon': return ';'
    case 'Quote': return "'"
    case 'Comma': return ','
    case 'Period': return '.'
    case 'Slash': return '/'
    case 'Backquote': return '`'
    case 'PageUp': return 'PgUp'
    case 'PageDown': return 'PgDn'
    default: return code
  }
}

/** The keys of a combination in reading order, ready to render one <kbd> each. */
export function comboKeys(combo: Combo, arrows: { left: string; right: string; up: string; down: string }): string[] {
  const p = parseCombo(combo)
  const keys: string[] = []
  if (p.ctrl) keys.push('Ctrl')
  if (p.alt) keys.push('Alt')
  if (p.shift) keys.push('Shift')
  keys.push(keyLabel(p.code, arrows))
  return keys
}

export function comboText(combo: Combo, arrows: { left: string; right: string; up: string; down: string }): string {
  return comboKeys(combo, arrows).join('+')
}
