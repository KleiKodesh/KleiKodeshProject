<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useDebounceFn } from '@vueuse/core'
import { WindowedScroller, type WindowedScrollerApi } from 'vue-windowed-scroller'
import type { PdfSearchHit, PdfSearchSnippet } from './usePdfSearchResults'
import { useInputListNavigation } from '@/composables/useInputListNavigation'

/**
 * The search-results panel: one row per match of the toolbar's current search, its page and the
 * text around it with the match marked; a click shows that match and makes it the current one.
 */
const props = defineProps<{
  hits: PdfSearchHit[]
  searching: boolean
  hasQuery: boolean
  /** The current match, 1-based in document order (0: none) - the same order as `hits`. */
  currentMatch: number
  snippetOf: (hit: PdfSearchHit) => PdfSearchSnippet | null
  /** A page's label for display (page labels when the document has them). */
  labelOf?: (n: number) => string
  /** The search text - the same one as the toolbar's search bar. */
  query: string
}>()
const emit = defineEmits<{ select: [hit: PdfSearchHit]; queryChange: [query: string]; next: [previous: boolean] }>()

// The panel's own search box, the same search as the toolbar's bar: typing searches (a short
// pause first, as the bar), Enter / Shift+Enter go to the next / previous match.
const draft = ref(props.query)
watch(() => props.query, (q) => { if (q !== draft.value) draft.value = q })
const sendQuery = useDebounceFn((q: string) => emit('queryChange', q), 250)
function onInput(e: Event) {
  draft.value = (e.target as HTMLInputElement).value
  void sendQuery(draft.value)
}
function onEnter(e: KeyboardEvent) {
  if (draft.value !== props.query) emit('queryChange', draft.value)
  else emit('next', e.shiftKey)
}

// The catalog's list keys (useInputListNavigation, the combobox model): focus stays in the
// search box, Up / Down / PageUp / PageDown / Ctrl+Home / Ctrl+End move a highlight through the
// results, Enter shows the highlighted one (as a click). With nothing highlighted, Enter keeps
// the box's own meaning (search / next match).
const scroller = ref<WindowedScrollerApi | null>(null)
const select = (index: number) => {
  const hit = props.hits[index]
  if (hit) emit('select', hit)
}
let lastNavIndex = -1
const { activeIndex, onKeydown: onListKeydown } = useInputListNavigation({
  getCount: () => props.hits.length,
  onActivate: (index) => select(index),
  // A windowed list: its own scroller lands on the row ('nearest': only when not fully shown).
  scrollIntoView: (index) => {
    const s = scroller.value
    const down = index >= lastNavIndex
    lastNavIndex = index
    if (s && !s.isRowFullyVisible(index)) void s.scrollToIndex(index, { align: down ? 'end' : 'start' })
  },
})
// A new search: the old highlight would point at another result. (Results streaming in for the
// same search only append, so the highlight stays on its row.)
watch(() => props.query, () => {
  activeIndex.value = -1
  lastNavIndex = -1
})
function onBoxKeydown(e: KeyboardEvent) {
  if (onListKeydown(e)) return
  if (e.key === 'Enter') {
    e.preventDefault()
    onEnter(e)
  }
}
function onRowClick(index: number) {
  activeIndex.value = index
  lastNavIndex = index
  select(index)
}

// The rows redraw when the list or the current match changes (its mark moves).
const rowsVersion = computed(() => [props.hits, props.currentMatch, activeIndex.value])
// A click, F3 / Shift+F3 move the current match: keep its row in view. Keyed on WHICH match is
// current, not its number: while results stream in, matches found on earlier pages raise the
// number of the same match, and that must not move the list.
const currentKey = computed(() => {
  const hit = props.hits[props.currentMatch - 1]
  return hit ? `${hit.pageIndex}:${hit.matchIndex}` : ''
})
watch(currentKey, (key) => {
  const index = props.currentMatch - 1
  if (key && scroller.value && !scroller.value.isRowFullyVisible(index)) {
    void scroller.value.scrollToIndex(index, { align: 'start' })
  }
})
</script>

<template>
  <div class="pdf-search-results">
    <div class="search-box">
      <input
        type="search"
        class="search-field"
        :dir="draft ? 'auto' : undefined"
        :value="draft"
        :placeholder="$t('pdfReader.panel.searchPlaceholder')"
        :aria-label="$t('pdfReader.panel.searchPlaceholder')"
        @input="onInput"
        @keydown="onBoxKeydown"
        @keydown.esc.stop
      />
    </div>
    <div v-if="!hasQuery" class="empty">{{ $t('pdfReader.panel.searchHint') }}</div>
    <template v-else>
      <div class="summary" aria-live="polite">
        {{ hits.length ? $t('pdfReader.panel.resultCount', { n: hits.length }) : searching ? $t('pdfReader.panel.searching') : $t('pdfReader.panel.noResults') }}
        <span v-if="searching && hits.length" class="busy">{{ $t('pdfReader.panel.searching') }}</span>
      </div>
      <WindowedScroller v-if="hits.length" ref="scroller" class="list" thin :count="hits.length" :row-source-version="rowsVersion">
        <template #row="{ index }">
          <button
            v-for="hit in hits.slice(index, index + 1)"
            :key="`${hit.pageIndex}:${hit.matchIndex}`"
            type="button"
            class="hit"
            :class="{ current: index === currentMatch - 1, 'is-focused': index === activeIndex }"
            tabindex="-1"
            @mousedown.prevent
            @click="onRowClick(index)"
          >
            <span class="page">{{ $t('pdfReader.panel.page', { n: labelOf?.(hit.pageIndex + 1) ?? hit.pageIndex + 1 }) }}</span>
            <template v-for="s in [snippetOf(hit)]" :key="0">
              <span v-if="s" class="snippet" dir="auto">{{ s.before }}<mark>{{ s.match }}</mark>{{ s.after }}</span>
              <span v-else class="snippet loading">&hellip;</span>
            </template>
          </button>
        </template>
      </WindowedScroller>
    </template>
  </div>
</template>

<style scoped>
.pdf-search-results {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
}
.pdf-search-results > .windowed-scroller {
  flex: 1;
  min-height: 0;
}
.search-box {
  flex-shrink: 0;
  padding: 6px 8px;
}
.search-field {
  width: 100%;
  box-sizing: border-box;
  height: 26px;
  padding: 0 8px;
  font-size: 12px;
  color: var(--text-primary);
  background: var(--input-bg, transparent);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  outline: none;
  user-select: text;
}
.search-field:focus {
  border-color: var(--accent-color);
}
.empty {
  padding: 10px;
  font-size: 12px;
  color: var(--text-secondary);
}
.summary {
  display: flex;
  gap: 8px;
  padding: 4px 10px;
  font-size: 11px;
  font-weight: 600;
  color: var(--text-secondary);
  background: var(--bg-toolbar);
  flex-shrink: 0;
}
.busy {
  font-weight: normal;
}
.hit {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 2px;
  width: 100%;
  height: auto;
  padding: 5px 10px;
  text-align: start;
  border-radius: 0;
  background: none;
}
.hit:hover {
  background: var(--hover-bg);
}
.hit.current {
  background: var(--hover-bg);
  box-shadow: inset 0 0 0 1px var(--border-color);
}
/* The keyboard highlight: the catalog list's look (BookCatalogView.List .is-focused). */
.hit.is-focused {
  background: var(--item-selected-bg);
  box-shadow: inset 0 0 0 1px var(--item-selected-border);
}
.page {
  font-size: 10.5px;
  color: var(--text-secondary);
}
.snippet {
  font-size: 12px;
  line-height: 1.35;
  color: var(--text-primary);
  unicode-bidi: isolate;
  overflow-wrap: anywhere;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.snippet.loading {
  color: var(--text-secondary);
}
mark {
  background: var(--accent-color);
  color: var(--bg-primary, #fff);
  border-radius: 2px;
  padding: 0 1px;
}
</style>
