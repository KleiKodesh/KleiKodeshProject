<script setup lang="ts">
import { ref, watch, computed, nextTick, type Component } from 'vue'
import {
  IconLayoutRowTwoFocusTop20Filled,
  IconLayoutRowTwoFocusBottom20Filled,
  IconLayoutColumnTwoFocusLeft20Filled,
  IconLayoutColumnTwoFocusRight20Filled,
  IconChevronUp20Regular,
  IconChevronDown20Regular,
  IconDismiss20Regular,
} from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useRecentSearches } from '@/composables/useRecentSearches'
import BookViewSearchRecentsDropdown from './BookViewSearchRecentsDropdown.vue'
import { searchModeForSlot, slotForSearchMode } from './bookViewTypes'
import type { CommentarySlot, SearchMode } from './bookViewTypes'

const props = defineProps<{
  visible: boolean
  toolbarPosition: 'top' | 'bottom' | 'right' | 'left'
  matchCount: number
  currentMatch: number
  /** True when either commentary panel is open — gates the mode button. */
  commentaryVisible: boolean
  /** The commentary panels currently open, in display order. */
  openCommentarySlots: CommentarySlot[]
  mode: SearchMode
  query?: string
}>()
const emit = defineEmits<{
  close: []
  queryChange: [string]
  next: []
  prev: []
  modeChange: [SearchMode]
}>()

const inputRef = ref<HTMLInputElement | null>(null)
const searchMode = ref<SearchMode>(props.mode)

/**
 * Recent queries, through the same pair the full-text-search bar uses: the
 * `useRecentSearches` controller and the <RecentSearchesDropdown> popup. Its own storage
 * key, so it is its own list — searching inside a book and searching the whole library
 * are different intents.
 *
 * The key is a constant rather than anything per-tab, which is what makes the history
 * global: every book-view bar in the app builds a controller over this one key, so a
 * phrase searched in one book is offered in the next. Ten entries, LRU, persisted.
 *
 * `recents.query` IS the input's model, so `inputValue` keeps its old meaning and the
 * watchers below are unchanged.
 */
const recents = useRecentSearches({ key: 'book-view-search', max: 10 })
const inputValue = recents.query
inputValue.value = props.query ?? ''

// One element, two owners: `inputRef` drives focusInput below, and the recents
// controller needs the same node for its own focus and outside-click handling.
function setInputEl(el: unknown) {
  inputRef.value = el instanceof HTMLInputElement ? el : null
  recents.setInput(el as Parameters<typeof recents.setInput>[0])
}

/**
 * Focus the field, and with `selectAll` also select what is in it so the next keystroke
 * replaces it rather than appending.
 *
 * Select-all is for a REOPEN, where the text is a query the user already ran and the
 * likely next move is a different search. It is deliberately NOT used when the bar stays
 * open and only its target changes (a mode switch, or re-picking the current mode): the
 * query is carried over verbatim there and may never have been searched at all, so
 * selecting it would put a half-typed term one keystroke from being wiped.
 *
 * select() goes after focus(): focusing places the caret and would collapse a selection
 * made beforehand.
 */
function focusInput({ selectAll = true }: { selectAll?: boolean } = {}) {
  const el = inputRef.value
  if (!el) return
  el.focus()
  if (selectAll) el.select()
}

watch(() => props.query, (q) => {
  const nextValue = q ?? ''
  if (nextValue !== inputValue.value) inputValue.value = nextValue
})
watch(() => props.mode, (m) => { if (searchMode.value !== m) searchMode.value = m })
watch(inputValue, (v) => emit('queryChange', v))
// A mode switch retargets WHERE we search, not WHAT — keep the caret, don't select.
watch(searchMode, (m) => { emit('modeChange', m); nextTick(() => focusInput({ selectAll: false })) })
// Reopen: select the retained query so the next keystroke replaces it. The nextTick
// matters: an open can change query and visible in the same tick, and select() needs the
// new value already written to inputValue by the props.query watch above — deferring past
// the flush guarantees that whatever order the watchers run in.
watch(() => props.visible, (v) => { if (v) nextTick(focusInput) })
// Fall back to content search when the panel being searched closes. Keyed by slot
// so closing one panel never disturbs a search running in the other.
watch(() => props.openCommentarySlots, (slots) => {
  const slot = slotForSearchMode(searchMode.value)
  if (slot && !slots.includes(slot)) searchMode.value = 'content'
}, { deep: true })

const isBottomAnchored = computed(() => props.toolbarPosition === 'bottom')

const placeholder = computed(() =>
  searchMode.value === 'content' ? 'חיפוש בטקסט...' : 'חיפוש במפרשים...',
)

const matchLabel = computed(() => {
  if (!inputValue.value) return ''
  if (props.matchCount === 0) return '0 / 0'
  if (props.matchCount > 0) return `${props.currentMatch + 1} / ${props.matchCount}`
  return ''
})

function onClose() {
  // Keep inputValue as-is: the query is an in-session, per-tab value that must
  // survive closing the bar so reopening restores it. Only the tab close (or a
  // fresh session) discards it. Clearing here would emit queryChange('') and wipe it.
  emit('close')
}

function onInput(event: Event) {
  inputValue.value = (event.target as HTMLInputElement).value
}

function onKeydown(event: KeyboardEvent) {
  // Recents first: while the list is open it owns the arrows, Enter (accept the
  // highlighted suggestion) and Escape (close the list), calling preventDefault on those
  // so the bar's own Enter/Escape below don't also fire on the same press.
  const acceptingSuggestion =
    event.key === 'Enter' && recents.open.value && recents.activeIndex.value >= 0
  recents.onKeydown(event)
  if (event.defaultPrevented) {
    // Accepting a suggestion is still an Enter, so it counts as running that query:
    // re-record it to move it back to the front of the list. Without this the entry
    // the user just chose keeps sinking while newer ones stay above it.
    if (acceptingSuggestion) recents.record()
    return
  }

  if (event.key === 'Enter') {
    // Enter is this bar's only submit — there is no search button — so it is the one
    // place a query is worth recording. Per keystroke would instead fill the list with
    // the prefixes of a single search.
    recents.record()
    event.shiftKey ? emit('prev') : emit('next')
  } else if (event.key === 'Escape') onClose()
}

// Unique per bar: split view mounts two, and duplicate DOM ids would cross-wire
// each field's aria-controls to the other pane's listbox.
const listboxId = `book-view-recents-${Math.random().toString(36).slice(2)}`

const RECENTS_TITLE = 'חיפושים אחרונים'

/**
 * The recents chevron — the ONLY thing that opens the list here. Focus and typing
 * deliberately do not (unlike the search page): this bar sits over the text the reader
 * is in, so the list appears when asked for and never on its own.
 *
 * `onFocus` is the composable's open-the-list entry point; nothing is wired to the
 * field's own focus event, so calling it here opens the list and nothing else does.
 *
 * The guard: the popup dismisses itself on any outside click and the chevron IS outside
 * it, so that dismiss lands before this handler and the click would reopen what the user
 * meant to close. Reading `open` on pointerdown, before the popup reacts, says which of
 * the two this click really was.
 *
 * A keyboard press (Tab to the button, then Enter/Space) fires click with NO preceding
 * pointerdown, so the flag would still hold whatever the last mouse press left — which
 * made the list impossible to close from the keyboard. `event.detail === 0` marks those
 * synthesised clicks; there the live `open` state is the truth. The flag is cleared
 * after every use so it can never leak into the next press.
 */
let wasOpenOnPress = false
// The listbox renders on open AND non-empty, so aria-expanded must track both or it
// advertises a list that is not in the DOM.
const recentsListVisible = computed(() => recents.open.value && recents.suggestions.value.length > 0)

function onChevronPress() {
  wasOpenOnPress = recents.open.value
}
function toggleRecents(event: MouseEvent) {
  const fromKeyboard = event.detail === 0
  const wasOpen = fromKeyboard ? recents.open.value : wasOpenOnPress
  wasOpenOnPress = false
  if (wasOpen) {
    recents.onBlur()
    return
  }
  recents.onFocus()
  inputRef.value?.focus()
}

const MODE_ICONS: Record<SearchMode, Component> = {
  content: IconLayoutRowTwoFocusTop20Filled,
  'commentary-bottom': IconLayoutRowTwoFocusBottom20Filled,
  'commentary-side': IconLayoutColumnTwoFocusRight20Filled,
  'commentary-side-left': IconLayoutColumnTwoFocusLeft20Filled,
}

// Labels reuse the toolbar's wording for the text zoom and the panel toggles.
const MODE_LABELS: Record<SearchMode, string> = {
  content: 'טקסט',
  'commentary-bottom': 'מפרשים למטה',
  'commentary-side': 'מפרשים מימין',
  'commentary-side-left': 'מפרשים משמאל',
}

// Offers the book text plus each open commentary panel, so the menu only ever
// lists a target the user can actually see.
const searchModeOptions = computed<SearchMode[]>(() => [
  'content',
  ...props.openCommentarySlots.map(searchModeForSlot),
])

const modeMenuOpen = ref(false)
const modeMenuRef = ref<HTMLElement | null>(null)
const modeBtnRef = ref<HTMLElement | null>(null)
const { justClosed } = useDropdownClose(modeMenuRef, () => (modeMenuOpen.value = false), {
  toggleButton: modeBtnRef,
})

// Closing the bar or the last panel unmounts the open menu via v-if, which
// useDropdownClose can't see; reset so it doesn't come back already open.
watch([() => props.visible, () => props.commentaryVisible], ([barVisible, hasPanels]) => {
  if (!barVisible || !hasPanels) modeMenuOpen.value = false
})

function toggleModeMenu() {
  if (justClosed.value) return
  modeMenuOpen.value = !modeMenuOpen.value
}

function selectSearchMode(mode: SearchMode) {
  const changed = mode !== searchMode.value
  searchMode.value = mode
  modeMenuOpen.value = false
  // The searchMode watch refocuses on a real change; this covers re-picking the mode
  // already active, where the watch never fires. Neither selects — see focusInput.
  if (!changed) nextTick(() => focusInput({ selectAll: false }))
}

defineExpose({ focus: focusInput })
</script>

<template>
  <Transition name="search-bar">
    <div v-if="visible" class="search-bar" :class="{ 'bottom-anchored': isBottomAnchored }">
      <div class="search-inner">
        <!-- data-ctrlf-enabled: Ctrl+F with the caret already in this field is a no-op
             (useAppTitleBarShortcuts defers to it) instead of falling through to
             openSearch, which would re-target the bar and select-all over a query the
             user is still typing. -->
        <input
          :ref="setInputEl"
          data-ctrlf-enabled
          :value="inputValue"
          type="search"
          class="search-input"
          :placeholder="placeholder"
          spellcheck="true"
          autocomplete="off"
          role="combobox"
          aria-autocomplete="list"
          :aria-expanded="recentsListVisible"
          :aria-controls="listboxId"
          @input="onInput"
          @blur="recents.onBlur"
          @keydown="onKeydown"
        />
        <span class="match-count" :class="{ 'no-match': props.matchCount === 0 }">{{ matchLabel }}</span>
        <!-- Recents chevron, and the only way to open the list. Shown only while the
             field is empty (with a query typed the reader is searching, not browsing
             what they searched before) AND there is history to show, so it is never a
             live-looking button that does nothing. -->
        <button
          v-if="!inputValue && recents.suggestions.value.length"
          class="recents-btn"
          :class="{ active: recentsListVisible }"
          :title="RECENTS_TITLE"
          @pointerdown="onChevronPress"
          @mousedown.prevent
          @click="toggleRecents"
        >
          <IconChevronDown20Regular />
        </button>
        <!-- Recent searches, as a combobox listbox belonging to the field: sized to
             the input and styled like the mode menu, not the search page's floating
             bubble. Rows carry an × to drop a single query. -->
        <BookViewSearchRecentsDropdown
          :controller="recents"
          :open-up="isBottomAnchored"
          :listbox-id="listboxId"
        />
      </div>

      <!-- Leading separator appears only with the mode toggle it introduces: the toggle
           is its own group (where the search runs). -->
      <span v-if="props.commentaryVisible" class="sep" />
      <div v-if="props.commentaryVisible" class="mode-dropdown">
        <button
          ref="modeBtnRef"
          class="mode-btn"
          :class="{ active: searchMode !== 'content' }"
          :title="MODE_LABELS[searchMode]"
          @click="toggleModeMenu"
        >
          <component :is="MODE_ICONS[searchMode]" />
        </button>
        <div v-if="modeMenuOpen" ref="modeMenuRef" class="mode-menu" :class="{ 'open-up': isBottomAnchored }">
          <button
            v-for="mode in searchModeOptions"
            :key="mode"
            class="mode-option"
            :class="{ selected: mode === searchMode }"
            @click="selectSearchMode(mode)"
          >
            <component :is="MODE_ICONS[mode]" />
            <span>{{ MODE_LABELS[mode] }}</span>
          </button>
        </div>
      </div>
      <!-- Always shown: this divides the field (and the mode toggle, when present) from
           the match navigation, which is never hidden. -->
      <span class="sep" />

      <button class="nav-btn" :disabled="props.matchCount === 0" @click="emit('prev')">
        <IconChevronUp20Regular />
      </button>
      <button class="nav-btn" :disabled="props.matchCount === 0" @click="emit('next')">
        <IconChevronDown20Regular />
      </button>

      <span class="sep" />
      <button class="close-btn" @click="onClose"><IconDismiss20Regular /></button>

      <slot name="panel" />

    </div>
  </Transition>
</template>

<style scoped>
/* Anchored to the pane's .content-area (position: relative), NOT the viewport:
   in split view each pane centers its own bar over its own content. */
.search-bar {
  position: absolute;
  z-index: 9999;
  top: 4px;
  left: 0;
  right: 0;
  margin: 0 auto;
  display: flex;
  align-items: center;
  gap: 2px;
  width: fit-content;
  /* Asymmetric like the reference widget (0 4px 0 9px): more room where the field
     starts, less where the buttons end. */
  padding: 2px 4px 2px 8px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-sizing: border-box;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.4), 0 1px 3px rgba(0, 0, 0, 0.25);
}

.search-inner {
  display: flex;
  align-items: center;
  padding: 1px 6px;
  gap: 4px;
  /* The global .search-inner is a tinted, bordered 999px pill — right for a search
     PAGE, wrong here. This is a find-in-page widget: VS Code's (and Chrome's) put the
     field straight into the widget with no inner container, and a pill inside the
     bar's own border reads as a box in a box. Strip it back to the bare field. */
  height: 24px;
  background: none;
  border: none;
  border-radius: 0;
  /* Positioning context for the recents listbox, which is sized to this field. */
  position: relative;
}

.search-input {
  width: 130px;
  border: none;
  background: none;
  outline: none;
  font-size: 13px;
  color: var(--text-primary);
  cursor: text;
  direction: rtl;
  /* Pinned, like the reference widget's 25px input row: without it the bar's height is
     whatever the glyphs happen to be, and it drifts between fonts and zoom levels. */
  height: 24px;
}

.search-input::placeholder { color: var(--text-secondary); }
.search-input::-webkit-search-cancel-button { filter: grayscale(1) opacity(0.4); }

.match-count {
  font-size: 11px;
  color: var(--text-secondary);
  white-space: nowrap;
  flex-shrink: 0;
  min-width: 32px;
  text-align: end;
}

.match-count.no-match { color: var(--status-danger); }

.sep {
  width: 1px;
  height: 16px;
  background: var(--border-color);
  flex-shrink: 0;
  margin-inline: 1px;
}

.nav-btn, .close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  flex-shrink: 0;
  border-radius: 4px;
  cursor: pointer;
}

.nav-btn svg, .close-btn svg { width: 16px; height: 16px; }
.nav-btn:disabled { opacity: 0.3; cursor: default; }

.search-bar.bottom-anchored {
  top: auto;
  bottom: 4px;
}

.search-bar-enter-active, .search-bar-leave-active {
  transition: opacity 150ms ease, transform 150ms ease;
}
.search-bar-enter-from, .search-bar-leave-to {
  opacity: 0;
  transform: translateY(-6px);
}
.bottom-anchored.search-bar-enter-from,
.bottom-anchored.search-bar-leave-to {
  opacity: 0;
  transform: translateY(6px);
}

.mode-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  border-radius: 4px;
  flex-shrink: 0;
  color: var(--text-secondary);
}
.mode-btn svg { width: 16px; height: 16px; }
.mode-btn.active { color: var(--accent-color); }

/* Sits inside the input group, so it reads as part of the field rather than as
   another toolbar button. */
.recents-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  flex-shrink: 0;
  border-radius: 4px;
  color: var(--text-secondary);
}
.recents-btn svg { width: 14px; height: 14px; }
.recents-btn.active { color: var(--accent-color); }

.mode-dropdown { position: relative; }

.mode-menu {
  position: absolute;
  top: calc(100% + 6px);
  inset-inline-end: 0;
  min-width: 140px;
  padding: 2px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25);
  z-index: 100;
}
.mode-menu.open-up {
  top: auto;
  bottom: calc(100% + 6px);
}

.mode-option {
  display: flex;
  align-items: center;
  gap: 6px;
  width: 100%;
  height: 28px;
  padding: 0 8px;
  border-radius: 4px;
  font-size: 12px;
  color: var(--text-primary);
  white-space: nowrap;
}
.mode-option svg { width: 16px; height: 16px; flex-shrink: 0; }
.mode-option:hover { background: color-mix(in srgb, var(--text-primary) 6%, transparent); }
.mode-option.selected { color: var(--accent-color); }
</style>
