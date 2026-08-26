/**
 * useRecentSearches — the app's own recent-searches list for a search input: the
 * popup of previously-run queries that drops under the field.
 *
 * NOT the browser's form autofill, and not a native <datalist>. Both are unavailable
 * here — the WebView2 host disables Chrome's built-in autofill — so this is a full
 * app-owned reimplementation, and it is what every search bar in the app uses to show
 * recent searches. Pair it with <RecentSearchesDropdown>, which draws the popup.
 *
 * Behaves like the browser's own recent-inputs dropdown:
 *   - focusing an empty field shows the full recent history
 *   - typing filters to case-insensitive PREFIX matches
 *   - ArrowDown/ArrowUp move a highlight (wrapping); nothing is preselected
 *   - Enter accepts the highlighted item (else lets the field's own Enter run)
 *   - Escape closes the dropdown (and only then lets the field's own Esc run)
 *   - Shift+Delete removes the highlighted item from history
 *   - history is deduped, newest-first, capped, and persisted to localStorage
 *
 * Usage:
 *   const recents = useRecentSearches({ key: 'fts-search' })
 *   // <input :ref="recents.setInput" v-model="recents.query"
 *   //   @focus="recents.onFocus" @input="recents.onInput"
 *   //   @keydown="recents.onKeydown" @blur="recents.onBlur" />
 *   // <RecentSearchesDropdown :controller="recents" />
 *   // commit an entry when the value is "submitted": recents.record()
 *
 * Inputs sharing a `key` share one stored list — that is how several instances of the
 * same bar (one per tab or pane) end up offering a single history.
 */
import { ref, computed, readonly, nextTick, type Ref, type ComponentPublicInstance } from 'vue'
import { lsGet, lsSet } from '@/utils/persistence'

/** What a Vue template `:ref` callback receives. */
type TemplateRefTarget = Element | ComponentPublicInstance | null

const LS_PREFIX = 'recent-searches.'
const DEFAULT_MAX = 15

export interface UseRecentSearchesOptions {
  /** Storage namespace — history is keyed by this, so inputs sharing a key share history. */
  key: string
  /** Max entries kept (default 15). */
  max?: number
  /**
   * Called when the user commits a suggestion (click or Enter) — the field's
   * "action". Use it to run the search/submit. NOT called for Tab, which only
   * completes the text so the user can keep editing.
   */
  onSelect?: (value: string) => void
}

export interface RecentSearchesController {
  /** Bind with v-model on the input. */
  query: Ref<string>
  /** The visible, filtered suggestion list (empty ⇒ dropdown hidden). */
  suggestions: Readonly<Ref<readonly string[]>>
  /** Whether the dropdown should be shown. */
  open: Readonly<Ref<boolean>>
  /** Index of the highlighted suggestion, or -1 for none. */
  activeIndex: Readonly<Ref<number>>
  /** Ref callback for the input element (used for positioning + focus). */
  setInput: (el: TemplateRefTarget) => void
  /**
   * Focus the input. With `{ silent: true }` the focus does NOT open the
   * dropdown — for programmatic focus (e.g. session restore) where a suggestion
   * bubble popping up unbidden would be jarring. A subsequent genuine focus,
   * click, or keystroke opens it as usual.
   * With `{ selectAll: true }` the existing text is selected, so the first
   * keystroke replaces it — for a restored query the user has already searched.
   * Ignored when the field is ALREADY focused: that means the user is in it and
   * possibly mid-word, and selecting would put their own typing one keystroke from
   * being wiped. (This focus runs several awaits into the restore, so the user can
   * beat it to the field.)
   */
  focus: (opts?: { silent?: boolean; selectAll?: boolean }) => void
  /** The input element (for the dropdown to anchor to). */
  inputEl: Readonly<Ref<HTMLInputElement | null>>
  onFocus: () => void
  onInput: () => void
  onBlur: () => void
  onKeydown: (e: KeyboardEvent) => void
  /** Apply a suggestion to the field and close (fill only — no action). */
  select: (item: string) => void
  /** Apply a suggestion and fire the field's action (onSelect). */
  commit: (item: string) => void
  /** Remove an entry from history. */
  remove: (item: string) => void
  /** Highlight a suggestion (e.g. on hover). */
  setActive: (index: number) => void
  /** Commit the current (or given) value to history — call on "submit". */
  record: (value?: string) => void
}

export function useRecentSearches(opts: UseRecentSearchesOptions): RecentSearchesController {
  const max = opts.max ?? DEFAULT_MAX
  const lsKey = LS_PREFIX + opts.key

  const query = ref('')
  const inputEl = ref<HTMLInputElement | null>(null)
  const open = ref(false)
  const activeIndex = ref(-1)
  const history = ref<string[]>(lsGet<string[]>(lsKey) ?? [])
  // Set by focus({ silent: true }); consumed by the next onFocus so a programmatic
  // focus (session restore) places the cursor without popping the suggestion bubble.
  let suppressNextFocusOpen = false

  const suggestions = computed<string[]>(() => {
    const q = query.value.trim().toLowerCase()
    if (!q) return history.value
    // Prefix match, case-insensitive; exclude an exact match (nothing to complete).
    return history.value.filter((h) => {
      const hl = h.toLowerCase()
      return hl.startsWith(q) && hl !== q
    })
  })

  function persist() {
    lsSet(lsKey, history.value)
  }

  function setInput(el: TemplateRefTarget) {
    inputEl.value = el instanceof HTMLInputElement ? el : null
  }

  function refreshOpen() {
    open.value = suggestions.value.length > 0
    if (activeIndex.value >= suggestions.value.length) activeIndex.value = -1
  }

  function focus(opts?: { silent?: boolean; selectAll?: boolean }) {
    if (opts?.silent) {
      suppressNextFocusOpen = true
      // Safety net: if .focus() fires no focus event (input already focused, or not
      // yet mounted) the flag would otherwise stay armed and swallow the next genuine
      // focus. The synchronous focus event, when it fires, clears it first; this just
      // disarms the leftover case on the next tick.
      nextTick(() => { suppressNextFocusOpen = false })
    }
    // Read "was it already focused" BEFORE focusing, or the answer is always yes.
    const el = inputEl.value
    const wasFocused = el != null && document.activeElement === el
    el?.focus()
    // select() after focus(): focusing an input places the caret and, in some
    // browsers, collapses any prior selection, so selecting first would be undone.
    if (opts?.selectAll && !wasFocused) el?.select()
  }

  function onFocus() {
    activeIndex.value = -1
    if (suppressNextFocusOpen) {
      suppressNextFocusOpen = false
      return
    }
    refreshOpen()
  }
  function onInput() {
    activeIndex.value = -1
    refreshOpen()
  }
  function onBlur() {
    // Item mousedown handlers preventDefault to keep focus, so a real blur means dismiss.
    open.value = false
    activeIndex.value = -1
  }

  function select(item: string) {
    query.value = item
    open.value = false
    activeIndex.value = -1
    inputEl.value?.focus()
  }

  function commit(item: string) {
    select(item)
    opts.onSelect?.(item)
  }

  function remove(item: string) {
    history.value = history.value.filter((x) => x !== item)
    persist()
    refreshOpen()
  }

  function setActive(index: number) {
    activeIndex.value = index
  }

  function record(value?: string) {
    const v = (value ?? query.value).trim()
    if (!v) return
    history.value = [v, ...history.value.filter((x) => x !== v)].slice(0, max)
    persist()
  }

  function onKeydown(e: KeyboardEvent) {
    const items = suggestions.value
    if (e.key === 'ArrowDown') {
      if (!items.length) return
      e.preventDefault()
      if (!open.value) { open.value = true; activeIndex.value = 0; return }
      activeIndex.value = (activeIndex.value + 1) % items.length
    } else if (e.key === 'ArrowUp') {
      if (!open.value || !items.length) return
      e.preventDefault()
      activeIndex.value = activeIndex.value <= 0 ? items.length - 1 : activeIndex.value - 1
    } else if (e.key === 'Enter') {
      if (open.value && activeIndex.value >= 0) {
        e.preventDefault()
        commit(items[activeIndex.value]!) // fill + fire the action
      } else {
        open.value = false // let the field's own Enter (e.g. run search) proceed
      }
    } else if (e.key === 'Tab') {
      // Autofill-style completion: Tab fills the highlighted item, or the top
      // match if none is highlighted. Only when there are suggestions and it's a
      // forward Tab — otherwise let Tab move focus normally.
      if (open.value && items.length && !e.shiftKey) {
        e.preventDefault()
        select(items[activeIndex.value >= 0 ? activeIndex.value : 0]!)
      } else {
        open.value = false
        activeIndex.value = -1
      }
    } else if (e.key === 'Escape') {
      if (open.value) {
        e.preventDefault()
        e.stopPropagation()
        open.value = false
        activeIndex.value = -1
      }
      // when already closed, let the field's own Esc handler run
    } else if (e.key === 'Delete' && e.shiftKey) {
      if (open.value && activeIndex.value >= 0) {
        e.preventDefault()
        const item = items[activeIndex.value]!
        const nextIdx = Math.min(activeIndex.value, items.length - 2)
        remove(item)
        activeIndex.value = suggestions.value.length ? Math.max(0, nextIdx) : -1
      }
    }
  }

  return {
    query,
    suggestions: readonly(suggestions),
    open: readonly(open),
    activeIndex: readonly(activeIndex),
    setInput,
    inputEl: readonly(inputEl) as Readonly<Ref<HTMLInputElement | null>>,
    focus,
    onFocus,
    onInput,
    onBlur,
    onKeydown,
    select,
    commit,
    remove,
    setActive,
    record,
  }
}
