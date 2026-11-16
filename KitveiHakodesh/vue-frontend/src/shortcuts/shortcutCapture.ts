/**
 * Records the next key combination the user presses, for the settings grid's "change" button.
 *
 * Imported FIRST in main.ts, so its keydown listener is the first capture-phase listener on
 * `window`: while a capture is running it sees the press before any shortcut handler and
 * swallows it (stopImmediatePropagation + preventDefault), so pressing Ctrl+K to choose it
 * does not also open the table of contents. Listeners on the same target run in the order
 * they were added, so being first is the whole mechanism - do not move the import.
 *
 * While no capture runs the listener returns at once and touches nothing.
 *
 * Esc cancels. Backspace or Delete, without modifiers, means "no shortcut". Lone modifier
 * presses are swallowed and waited past (the user is still building the combination).
 *
 * Inside Word, a combination that Word or another program takes first never arrives here,
 * so it cannot be recorded - which is what keeps the grid from accepting a shortcut that
 * could never fire (see UNREACHABLE_IN_WORD in shortcutCombo.ts for the measured list).
 */
import { ref, readonly } from 'vue'
import { comboFromEvent, type Combo } from './shortcutCombo'

export interface CaptureHandlers {
  onCombo: (combo: Combo) => void
  onClear: () => void
  onCancel: () => void
}

const active = ref<string | null>(null)
let handlers: CaptureHandlers | null = null

/** Id of the row being captured, or null. */
export const capturingId = readonly(active)

export function startCapture(id: string, h: CaptureHandlers): void {
  handlers?.onCancel()
  active.value = id
  handlers = h
}

export function stopCapture(): void {
  active.value = null
  handlers = null
}

function onKeydown(e: KeyboardEvent): void {
  if (!handlers) return
  e.preventDefault()
  e.stopImmediatePropagation()
  if (e.repeat) return
  const h = handlers
  const bare = !e.ctrlKey && !e.altKey && !e.shiftKey && !e.metaKey
  if (bare && e.code === 'Escape') {
    stopCapture()
    h.onCancel()
    return
  }
  if (bare && (e.code === 'Backspace' || e.code === 'Delete')) {
    stopCapture()
    h.onClear()
    return
  }
  const combo = comboFromEvent(e)
  if (!combo) return
  stopCapture()
  h.onCombo(combo)
}

// keyup/keypress of the captured press must not reach the app either (a keyup-driven
// handler would see half a shortcut). Swallowed only while capturing, and for one beat
// after it ends, which the setTimeout below gives.
let swallowUntil = 0
function onKeyOther(e: KeyboardEvent): void {
  if (!handlers && performance.now() > swallowUntil) return
  e.preventDefault()
  e.stopImmediatePropagation()
}

if (typeof window !== 'undefined') {
  window.addEventListener('keydown', (e) => {
    const wasCapturing = handlers != null
    onKeydown(e)
    if (wasCapturing) swallowUntil = performance.now() + 500
  }, { capture: true })
  window.addEventListener('keyup', onKeyOther, { capture: true })
  window.addEventListener('keypress', onKeyOther, { capture: true })
}
