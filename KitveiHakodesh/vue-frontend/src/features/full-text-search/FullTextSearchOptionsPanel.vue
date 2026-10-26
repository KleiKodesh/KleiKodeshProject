<script setup lang="ts">
import { computed, ref } from 'vue'
import { IconDismiss20Regular } from '@iconify-prerendered/vue-fluent'
import { WORD_OPTIONS, WRAP_OPTIONS, isWrapOption, optionsConflict, parseQueryWord, splitQuery } from './queryWordSyntax'
import type { AllWords, PerWord, QueryWord, WordOption } from './queryWordSyntax'

const props = defineProps<{
  maxWordDistance: number
  requireOrdered: boolean
  contextWords: number
  /** Each option's persisted "all words" setting. */
  allWords: AllWords
  /** The per-word choices (see PerWord). */
  perWord: PerWord
  /** Live search-bar text; only read for its words, never written. */
  query: string
  /** Rendered inline as a centered card on the empty search page, not as a popup. */
  asCard?: boolean
}>()

const emit = defineEmits<{
  'update:maxWordDistance': [number]
  'update:requireOrdered': [boolean]
  'update:contextWords': [number]
  setAllWords: [WordOption, boolean]
  'update:perWord': [PerWord]
  close: []
}>()

type ActiveTab = 'options' | 'syntax'
const activeTab = ref<ActiveTab>('options')

// Each option row: [all words | per word], at most one on.
//   - "All words" is the persisted setting.
//   - "Per word" picks words of the query; the choice is kept per tab.
// Neither reads nor writes the typed signs: the toggles are added to the words at search
// time, and a word's typed signs win over them (see applyWordOptions). Wrap kinds that
// conflict exclude each other: in "all words" across rows, and per word on the same word.
const LABEL_KEYS: Record<WordOption, string> = {
  wildcardPrefix: 'wildcardPrefix', wildcardSuffix: 'wildcardSuffix',
  grammarPrefix: 'grammarPrefix', grammarSuffix: 'grammarSuffix',
  fuzzy: 'fuzzy', ketiv: 'spellingVariants', inflections: 'inflections', synonyms: 'synonyms',
}

// The search term's words, by their bare text (the '@' book filter is not words); words
// with syntax beyond the toggles get no toggle.
const queryWords = computed(() => [...new Set(splitQuery(props.query).tokens
  .map(parseQueryWord).filter((w): w is QueryWord => w !== null).map((w) => w.core))])

const isAllWords = (option: WordOption) => props.allWords[option]
const isPerWord = (option: WordOption) => !isAllWords(option) && props.perWord[option] !== undefined
const wordHas = (core: string, option: WordOption) => !!props.perWord[option]?.includes(core)

function setPerWord(option: WordOption, words: string[] | undefined) {
  const next = { ...props.perWord }
  if (words) next[option] = words
  else delete next[option]
  emit('update:perWord', next)
}

function toggleAllWords(option: WordOption) {
  const on = !isAllWords(option)
  emit('setAllWords', option, on)
  if (!on) return
  setPerWord(option, undefined)
  for (const other of WRAP_OPTIONS)
    if (optionsConflict(other, option) && isAllWords(other)) emit('setAllWords', other, false)
}

function togglePerWord(option: WordOption) {
  if (isPerWord(option)) { setPerWord(option, undefined); return }
  // From "all words": keep every word checked.
  const fromAll = isAllWords(option)
  if (fromAll) emit('setAllWords', option, false)
  setPerWord(option, fromAll ? [...queryWords.value] : [])
}

function toggleWord(core: string, option: WordOption) {
  const on = !wordHas(core, option)
  const next = { ...props.perWord }
  const words = next[option] ?? []
  next[option] = on ? [...words, core] : words.filter((w) => w !== core)
  if (on && isWrapOption(option))
    for (const other of WRAP_OPTIONS)
      if (optionsConflict(other, option) && next[other]?.includes(core))
        next[other] = next[other]!.filter((w) => w !== core)
  emit('update:perWord', next)
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
              @click="setPerWord(option, [])"
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
          <div class="intent">{{ $t('fullTextSearch.options.syntaxInflections') }}</div>
          <div class="usage"><span class="example">שלום+</span></div>
        </div>
        <div class="syntax-row">
          <div class="intent">{{ $t('fullTextSearch.options.syntaxSynonyms') }}</div>
          <div class="usage"><span class="example">שלום=</span> <span class="example">שלום+=</span></div>
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
  /* Fills the full height under the bar. The page measures it (the containing block,
     .search-bar-dock, is sized by its content, so a percentage would resolve against
     the bar); 60vh is the fallback when no host sets it. */
  height: var(--options-panel-height, 60vh);
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
  height: auto;
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
.options-panel {
  /* One width for every control (inputs, yes/no, all words / per word, clear), so the
     columns line up; fits the longest label in both locales. */
  --control-width: 76px;
}
.distance-input {
  width: var(--control-width);
  height: 26px;
  font-size: 12px;
}
.toggle-btn {
  height: 26px;
  flex: none;
  width: var(--control-width);
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
  width: auto;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
