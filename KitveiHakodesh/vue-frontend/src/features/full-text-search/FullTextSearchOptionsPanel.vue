<script setup lang="ts">
import { computed, ref } from 'vue'
import { IconDismiss20Regular } from '@iconify-prerendered/vue-fluent'
import {
  WORD_OPTIONS, WRAP_OPTIONS, isWrapOption, parseQueryWord, renderQueryWord, hasOption, withOption,
} from './queryWordSyntax'
import type { QueryWord, WordOption } from './queryWordSyntax'

const props = defineProps<{
  maxWordDistance: number
  requireOrdered: boolean
  contextWords: number
  expandKetiv: boolean
  expandRelated: boolean
  grammarWrap: boolean
  fuzzy: boolean
  wildcardWrap: boolean
  /** Live search-bar text. It holds the per-word options as syntax (see queryWordSyntax). */
  query: string
  /** Rendered inline as a centered card on the empty search page, not as a popup. */
  asCard?: boolean
}>()

const emit = defineEmits<{
  'update:maxWordDistance': [number]
  'update:requireOrdered': [boolean]
  'update:contextWords': [number]
  'update:expandKetiv': [boolean]
  'update:expandRelated': [boolean]
  'update:grammarWrap': [boolean]
  'update:fuzzy': [boolean]
  'update:wildcardWrap': [boolean]
  'update:query': [string]
  close: []
}>()

type ActiveTab = 'options' | 'syntax'
const activeTab = ref<ActiveTab>('options')

// Each option row: [all words | per word], a pair where at most one is on. "All words" is
// the persisted global setting; "per word" writes the option into the query as syntax.
const LABEL_KEYS: Record<WordOption, string> = {
  wildcard: 'wildcardWrap', grammar: 'grammarWrap', fuzzy: 'fuzzy', ketiv: 'spellingVariants', related: 'expanded',
}
const GLOBAL_EVENTS = {
  wildcard: 'update:wildcardWrap', grammar: 'update:grammarWrap', fuzzy: 'update:fuzzy',
  ketiv: 'update:expandKetiv', related: 'update:expandRelated',
} as const

function isAllWords(option: WordOption): boolean {
  return { wildcard: props.wildcardWrap, grammar: props.grammarWrap, fuzzy: props.fuzzy,
           ketiv: props.expandKetiv, related: props.expandRelated }[option]
}
function setAllWords(option: WordOption, on: boolean) {
  // emit's overloads cannot take a union of event names.
  ;(emit as (event: string, value: boolean) => void)(GLOBAL_EVENTS[option], on)
}

// The query's tokens; words whose syntax the toggles can't express stay as typed.
const tokens = computed(() => props.query.trim().split(/\s+/).filter(Boolean))
const parsedTokens = computed(() => tokens.value.map(parseQueryWord))
const queryWords = computed(() => [
  ...new Set(parsedTokens.value.filter((w): w is QueryWord => w !== null).map((w) => w.core)),
])

// Rows switched to "per word" before any word is checked; one with a checked word shows by
// that alone. Not persisted: the query's syntax is the per-search state.
const perWordRows = ref(new Set<WordOption>())
function isPerWord(option: WordOption): boolean {
  if (isAllWords(option)) return false
  return perWordRows.value.has(option) || parsedTokens.value.some((w) => w && hasOption(w, option))
}

function wordHas(core: string, option: WordOption): boolean {
  return parsedTokens.value.some((w) => w?.core === core && hasOption(w, option))
}

/** Rewrite the query, mapping each parseable word. */
function rewrite(map: (word: QueryWord) => QueryWord) {
  const text = tokens.value
    .map((token, i) => {
      const word = parsedTokens.value[i]
      return word ? renderQueryWord(map(word)) : token
    })
    .join(' ')
  if (text !== tokens.value.join(' ')) emit('update:query', text)
}

function markPerWord(option: WordOption, on: boolean) {
  const rows = new Set(perWordRows.value)
  if (on) rows.add(option)
  else rows.delete(option)
  perWordRows.value = rows
}

/** A wrap row that was "all words" becomes "per word" with every word but `except` checked,
 *  so turning one word to another wrap never contradicts it. */
function splitOtherWrapRows(option: WordOption, except: string | null): ((w: QueryWord) => QueryWord)[] {
  if (!isWrapOption(option)) return []
  const maps: ((w: QueryWord) => QueryWord)[] = []
  for (const other of WRAP_OPTIONS) {
    if (other === option || !isAllWords(other)) continue
    setAllWords(other, false)
    markPerWord(other, true)
    maps.push((w) => (w.core === except || w.wrap ? w : withOption(w, other, true)))
  }
  return maps
}

function toggleAllWords(option: WordOption) {
  const on = !isAllWords(option)
  setAllWords(option, on)
  markPerWord(option, false)
  if (on && isWrapOption(option))
    for (const other of WRAP_OPTIONS) if (other !== option) { setAllWords(other, false); markPerWord(other, false) }
  // All words (or none): the option's per-word syntax is redundant, so drop it, and with
  // a wrap made global, every other wrap goes too.
  rewrite((w) => (on && isWrapOption(option) ? (w.wrap ? withOption(w, w.wrap, false) : w) : withOption(w, option, false)))
}

function togglePerWord(option: WordOption) {
  if (isPerWord(option)) {
    markPerWord(option, false)
    rewrite((w) => withOption(w, option, false))
    return
  }
  // From "all words": keep every word checked, now as syntax.
  const fromAll = isAllWords(option)
  setAllWords(option, false)
  markPerWord(option, true)
  if (!fromAll) return
  if (isWrapOption(option))
    for (const other of WRAP_OPTIONS) if (other !== option) setAllWords(other, false)
  rewrite((w) => withOption(w, option, true))
}

function toggleWord(core: string, option: WordOption) {
  const on = !wordHas(core, option)
  const others = on ? splitOtherWrapRows(option, core) : []
  rewrite((w) => {
    let next = w
    for (const map of others) next = map(next)
    return next.core === core ? withOption(next, option, on) : next
  })
}

function clearRow(option: WordOption) {
  rewrite((w) => withOption(w, option, false))
}

function onDistanceInput(event: Event) {
  const value = parseInt((event.target as HTMLInputElement).value, 10)
  if (!isNaN(value) && value >= 0) emit('update:maxWordDistance', value)
}

function onContextWordsInput(event: Event) {
  const value = parseInt((event.target as HTMLInputElement).value, 10)
  if (!isNaN(value) && value >= 0) emit('update:contextWords', value)
}
</script>

<template>
  <div class="options-panel" :class="{ 'as-card': asCard }" @keydown.esc.stop="emit('close')">
    <div class="panel-header">
      <div class="tabs">
        <button
          class="tab-btn"
          :class="{ active: activeTab === 'options' }"
          @click="activeTab = 'options'"
        >
          {{ $t('fullTextSearch.options.title') }}
        </button>
        <button
          class="tab-btn"
          :class="{ active: activeTab === 'syntax' }"
          @click="activeTab = 'syntax'"
        >
          {{ $t('fullTextSearch.options.syntax') }}
        </button>
      </div>
      <button v-if="!asCard" class="close-btn" :title="$t('fullTextSearch.close')" @click="emit('close')">
        <IconDismiss20Regular />
      </button>
    </div>

    <div v-if="activeTab === 'options'" class="panel-body">
      <!-- Word distance -->
      <div class="option-row">
        <label class="option-label" for="word-distance-input">{{ $t('fullTextSearch.options.wordDistance') }}</label>
        <input
          id="word-distance-input"
          type="number"
          class="distance-input"
          :value="props.maxWordDistance"
          min="0"
          max="9999"
          @input="onDistanceInput"
        />
      </div>

      <!-- Context words -->
      <div class="option-row">
        <label class="option-label" for="context-words-input">{{ $t('fullTextSearch.options.contextWords') }}</label>
        <input
          id="context-words-input"
          type="number"
          class="distance-input"
          :value="props.contextWords"
          min="0"
          max="9999"
          @input="onContextWordsInput"
        />
      </div>

      <!-- Order mode -->
      <div class="option-row">
        <span class="option-label">{{ $t('fullTextSearch.options.wordOrder') }}</span>
        <div class="toggle-group">
          <button
            class="toggle-btn"
            :class="{ active: !props.requireOrdered }"
            @click="emit('update:requireOrdered', false)"
          >{{ $t('fullTextSearch.options.no') }}</button>
          <button
            class="toggle-btn"
            :class="{ active: props.requireOrdered }"
            @click="emit('update:requireOrdered', true)"
          >{{ $t('fullTextSearch.options.yes') }}</button>
        </div>
      </div>

      <div class="option-separator" />

      <!-- Per-word options: [all words | per word]; per word lists the query's words. -->
      <div v-for="option in WORD_OPTIONS" :key="option" class="wrap-row">
        <div class="option-row">
          <span class="option-label">{{ $t(`fullTextSearch.options.${LABEL_KEYS[option]}`) }}</span>
          <div class="toggle-group">
            <button
              v-if="isPerWord(option) && queryWords.some((w) => wordHas(w, option))"
              class="toggle-btn"
              @click="clearRow(option)"
            >{{ $t('fullTextSearch.options.clear') }}</button>
            <button
              class="toggle-btn"
              :class="{ active: isAllWords(option) }"
              @click="toggleAllWords(option)"
            >{{ $t('fullTextSearch.options.allWords') }}</button>
            <button
              class="toggle-btn"
              :class="{ active: isPerWord(option) }"
              @click="togglePerWord(option)"
            >{{ $t('fullTextSearch.options.perWord') }}</button>
          </div>
        </div>
        <div v-if="isPerWord(option)" class="word-toggles">
          <span v-if="!queryWords.length" class="option-hint">{{ $t('fullTextSearch.options.perWordEmpty') }}</span>
          <button
            v-for="word in queryWords"
            :key="word"
            class="toggle-btn word-toggle"
            :class="{ active: wordHas(word, option) }"
            @click="toggleWord(word, option)"
          >{{ word }}</button>
        </div>
      </div>
    </div>

    <div v-else-if="activeTab === 'syntax'" class="syntax-help">
      <div class="syntax-reference">
        <div class="syntax-row">
          <div class="intent">{{ $t('fullTextSearch.options.syntaxWildcard') }}</div>
          <div class="usage"><span class="example">ישר*</span> <span class="example">*לום</span></div>
        </div>
        <div class="syntax-row">
          <div class="intent">{{ $t('fullTextSearch.options.syntaxOptional') }}</div>
          <div class="usage"><span class="example">שלו?ם</span></div>
        </div>
        <div class="syntax-row">
          <div class="intent">{{ $t('fullTextSearch.options.syntaxFuzzy') }}</div>
          <div class="usage"><span class="example">יצחק~</span></div>
        </div>
        <div class="syntax-row">
          <div class="intent">{{ $t('fullTextSearch.options.syntaxFuzzyN') }}</div>
          <div class="usage"><span class="example">משה~2</span> <span class="example">משה~3</span></div>
        </div>
        <div class="syntax-row">
          <div class="intent">{{ $t('fullTextSearch.options.syntaxOr') }}</div>
          <div class="usage"><span class="example">משה | אהרן</span></div>
        </div>
        <div class="syntax-row">
          <div class="intent">{{ $t('fullTextSearch.options.syntaxGrammar') }}</div>
          <div class="usage"><span class="example">%שלום%</span> <span class="example">%שלום</span> <span class="example">שלום%</span></div>
        </div>
        <div class="syntax-row">
          <div class="intent">{{ $t('fullTextSearch.options.syntaxKetiv') }}</div>
          <div class="usage"><span class="example">^שלום</span></div>
        </div>
        <div class="syntax-row">
          <div class="intent">{{ $t('fullTextSearch.options.syntaxRelated') }}</div>
          <div class="usage"><span class="example">+שלום</span></div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.options-panel {
  display: flex;
  flex-direction: column;
  background: var(--bg-secondary);
  /* A popup below the search bar at every width (narrow screens included), so it
     behaves as a real dropdown over the results instead of pushing them down. */
  position: absolute;
  inset-inline-start: 8px;
  top: 100%;
  z-index: 20;
  width: 360px;
  max-width: calc(100% - 16px);
  /* Viewport units, NOT a percentage: the containing block (.search-bar-dock)
     is sized by its content, so a percentage would resolve against the bar. */
  max-height: min(60vh, 420px);
  min-height: 0;
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 4px 16px rgb(0 0 0 / 24%);
  overflow: hidden;
}

/* Empty-page card: in-flow, bordered, centered by the page. Doubled class overrides the
   popup rule above. */
.options-panel.as-card {
  position: static;
  /* Drop the popup's z-index: as a flex item of the page card it still applies while
     static, and stacked the card above the narrow filter overlay (z-index 10). */
  z-index: auto;
  width: 380px;
  max-width: calc(100% - 32px);
  /* No cap of its own: the page card around it bounds the height (up to the full
     results area) and the body scrolls past that. */
  max-height: none;
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 4px 16px rgb(0 0 0 / 12%);
}
.panel-header {
  display: flex;
  align-items: stretch;
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
  background: var(--bg-secondary);
  height: 30px;
}
.tabs {
  display: flex;
  flex: 1;
  align-items: stretch;
  overflow: hidden;
}
.tab-btn {
  display: flex;
  align-items: center;
  padding: 0 12px;
  font-size: 11px;
  font-weight: 500;
  color: var(--text-secondary);
  border-bottom: 2px solid transparent;
  border-radius: 0;
  white-space: nowrap;
  transition: color 100ms, border-color 100ms;
  margin-bottom: -1px;
}
.tab-btn:hover:not(.active) {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 5%, transparent);
}
.tab-btn.active {
  color: var(--accent-color);
  border-bottom-color: var(--accent-color);
  background: none;
}
.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 30px;
  flex-shrink: 0;
  border-radius: 0;
  color: var(--text-secondary);
}
.panel-body {
  padding: 8px 12px 10px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  /* When the panel hits its max-height (short windows), the option list
     scrolls instead of overflowing past the panel box. */
  min-height: 0;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}
.option-separator {
  height: 1px;
  background: var(--border-color);
  margin: 4px 0;
  flex-shrink: 0;
  transform: scaleY(0.5);
  opacity: 0.7;
}
.option-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  min-height: 26px;
}
.option-label {
  font-size: 12px;
  color: var(--text-primary);
  flex-shrink: 0;
}
.option-hint {
  color: var(--text-secondary);
  font-size: 11px;
  margin-inline-start: 4px;
}
.distance-input {
  width: 56px;
  height: 24px;
  padding: 0 6px;
  border-radius: 4px;
  border: 1px solid var(--border-color);
  background: var(--input-bg);
  color: var(--text-primary);
  font-size: 12px;
  text-align: center;
  direction: ltr;
}
.distance-input:focus {
  outline: none;
  border-color: var(--accent-color);
}
.toggle-group {
  display: flex;
  gap: 4px;
}
.toggle-btn {
  flex: 1;
  height: 24px;
  padding: 0 10px;
  border: 1px solid var(--border-color);
  background: var(--bg-secondary);
  color: var(--text-primary);
  font-size: 11px;
  white-space: nowrap;
  border-radius: 4px;
  transition: all 100ms ease;
}
.toggle-btn:hover:not(.active) {
  background: color-mix(in srgb, var(--text-primary) 6%, var(--bg-secondary));
}
.toggle-btn.active {
  background: var(--accent-color);
  color: #fff;
  border-color: var(--accent-color);
}
/* Sized to the search bar (TopSearchBar: 30px pill, 13px text) so the panel reads as
   one scale with it, in both the dropdown and the empty-page card. Placed after the
   base rules above so these win. */
.option-row {
  min-height: 28px;
}
.option-label,
.intent {
  font-size: 13px;
}
.option-hint,
.tab-btn,
.example {
  font-size: 11.5px;
}
.distance-input {
  width: 64px;
  height: 26px;
  font-size: 12px;
}
.toggle-btn {
  height: 26px;
  /* Same width as the number inputs, so every control column lines up. */
  min-width: 64px;
  font-size: 12px;
}
.syntax-row {
  padding: 6px 8px;
}
.syntax-help {
  padding: 8px 12px 10px;
  overflow-y: auto;
  flex: 1;
  min-height: 0;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}
.syntax-reference {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.syntax-row {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 6px 8px;
  border-radius: 4px;
  background: color-mix(in srgb, var(--text-primary) 2%, transparent);
  border: 1px solid color-mix(in srgb, var(--border-color) 60%, transparent);
}
.intent {
  flex: 0 0 auto;
  min-width: 140px;
  font-size: 11px;
  font-weight: 500;
  color: var(--text-primary);
  line-height: 1.4;
}
.usage {
  flex: 1;
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
}
.example {
  font-family: 'Consolas', 'Courier New', monospace;
  color: var(--accent-color);
  font-size: 10px;
  background: color-mix(in srgb, var(--accent-color) 8%, transparent);
  padding: 2px 4px;
  border-radius: 3px;
  white-space: nowrap;
}

.wrap-row {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.word-toggles {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
/* Word toggles size to their word and may shrink below it on a narrow panel. */
.toggle-btn.word-toggle {
  flex: 0 1 auto;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
