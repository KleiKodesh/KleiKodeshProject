<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import CommentaryTreeSectionNode from './CommentaryTreeSectionNode.vue'
import { IconDismiss12Regular } from '@iconify-prerendered/vue-fluent'
import { useIntervalFn } from '@vueuse/core'
import type { CommentaryGroup } from './useCommentary'
import type { CommentaryTreeState, CommentaryVisibilityItem } from '../bookViewTypes'
import { useCommentaryTreeSearch } from './useCommentaryTreeSearch'
import {
  setCommentaryBookChecked,
  setCommentaryNodeChecked,
  setCommentaryAllChecked,
  isCommentaryBookUnchecked,
} from './uncheckedCommentaryBooks'

const props = defineProps<{
  groups: CommentaryGroup[]
  treeState: CommentaryTreeState // reactive object owned by one commentary panel, mutated directly
  /** Check-tree scope of the panel this tree is filtering (see commentaryScopeKey). */
  scopeKey: string
  scrollToBook: (bookId: number) => void
}>()

const emit = defineEmits<{ close: [] }>()

/** "Close the commentator tree" */
const closeTitle = 'סגור עץ מפרשים'

const searchInputRef = ref<HTMLInputElement | null>(null)
onMounted(() => nextTick(() => searchInputRef.value?.focus({ preventScroll: true })))

// ── Animated placeholder ──────────────────────────────────────────────────────
const PLACEHOLDERS = ['\u05E8\u05E9\u05D9', '\u05E8\u05E9\u05D1\u05DD @ \u05E8\u05E9\u05D9', '\u05DE\u05E4\u05E8\u05E9\u05D9\u05DD \u05E8\u05D0\u05E9\u05D5\u05E0\u05D9\u05DD']
const placeholder = ref(PLACEHOLDERS[0]!)
let phraseIndex = 0, charIndex = 0, pauseTicks = 0

const { pause: pauseTyping, resume: resumeTyping } = useIntervalFn(() => {
  if (pauseTicks > 0) { pauseTicks--; return }
  const target = PLACEHOLDERS[phraseIndex]!
  if (charIndex < target.length) {
    placeholder.value = target.slice(0, ++charIndex)
  } else {
    pauseTicks = 14
    phraseIndex = (phraseIndex + 1) % PLACEHOLDERS.length
    charIndex = 0
  }
}, 80)

watch(
  [() => props.treeState.searchQuery, () => props.treeState.tokens.length],
  ([query, tokenCount]) => {
    if (query || tokenCount) pauseTyping()
    else resumeTyping()
  },
)

// ── Search / tree logic ───────────────────────────────────────────────────────
// One tree instance per commentary panel, so scopeKey is fixed by construction:
// it is commentaryScopeKey(tabId, slot) and neither part can change under a live
// instance. Read once rather than reactively - it used to be a shared instance
// re-pointed between panels, and only a `key` kept that honest.
const scopeKey = props.scopeKey
const { syncVisibilityList, applyFilter, isSearching, tree, searchResults } =
  useCommentaryTreeSearch(() => props.groups, props.treeState, scopeKey)

watch(() => props.groups, syncVisibilityList, { immediate: true })
watch(() => props.treeState.visibilityList, () => { if (isSearching.value) applyFilter() })
watch(() => [props.treeState.searchQuery, props.treeState.tokens] as const, applyFilter)

// ── Token management ──────────────────────────────────────────────────────────
function commitToken() {
  const text = props.treeState.searchQuery.trim()
  if (!text) return
  props.treeState.tokens = [...props.treeState.tokens, text]
  props.treeState.searchQuery = ''
}

function removeToken(index: number) {
  props.treeState.tokens = props.treeState.tokens.filter((_, i) => i !== index)
  nextTick(() => searchInputRef.value?.focus())
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter' || event.key === '@') {
    event.preventDefault()
    commitToken()
    return
  }
  if (event.key === 'Backspace' && props.treeState.searchQuery === '' && props.treeState.tokens.length > 0) {
    event.preventDefault()
    removeToken(props.treeState.tokens.length - 1)
  }
}

// ── "הצג הכל" row ─────────────────────────────────────────────────────────────
const scopedItems = computed(() =>
  isSearching.value
    ? props.treeState.visibilityList.filter((item) => item.isInSearchResults)
    : props.treeState.visibilityList,
)

const allState = computed<'checked' | 'unchecked' | 'indeterminate'>(() => {
  const items = scopedItems.value
  if (!items.length) return 'unchecked'
  const checkedCount = items.filter((item) => item.isChecked).length
  if (checkedCount === items.length) return 'checked'
  if (checkedCount === 0) return 'unchecked'
  return 'indeterminate'
})

// After any store write, re-derive isChecked for the current visibility list
// so the tree reflects the virtual check-tree (defaults + overrides).
function syncCheckedFromStore() {
  for (const item of props.treeState.visibilityList) {
    item.isChecked = !isCommentaryBookUnchecked(scopeKey, item.sectionLabel, item.subSectionLabel, item.bookId)
  }
}

function toggleAll() {
  const shouldCheck = allState.value !== 'checked'
  if (isSearching.value) {
    // Search mode scopes "הצג הכל" to the matched books only — book-level ops.
    scopedItems.value.forEach((item) => {
      if (item.isChecked !== shouldCheck) toggleItem(item)
    })
    return
  }
  setCommentaryAllChecked(scopeKey, shouldCheck)
  syncCheckedFromStore()
}

function toggleNode(payload: { sectionLabel: string; subSectionLabel: string | null; shouldCheck: boolean }) {
  // Toggling a category cascades to all its children — present and future.
  setCommentaryNodeChecked(scopeKey, payload.sectionLabel, payload.subSectionLabel, payload.shouldCheck)
  syncCheckedFromStore()
}

function toggleItem(item: CommentaryVisibilityItem) {
  // A book override persists across lines regardless of its parent's state —
  // e.g. re-checking one book inside an unchecked category keeps it visible.
  setCommentaryBookChecked(scopeKey, item.sectionLabel, item.subSectionLabel, item.bookId, !item.isChecked)
  syncCheckedFromStore()
}
</script>

<template>
  <div class="filter-panel">
    <div class="tree-scroll">
      <div
        class="all-row"
        :class="{ checked: allState === 'checked', indeterminate: allState === 'indeterminate' }"
        @click="toggleAll"
      >
        <span class="check-col">
          <span class="check-mark">&#10003;</span>
          <span class="dash-mark">&#8211;</span>
        </span>
        <span class="row-label">&#x5D4;&#x5E6;&#x5D2; &#x5D4;&#x5DB;&#x5DC;</span>
        <!-- click.stop: the whole row is a toggle-all target. -->
        <button
          class="close-btn c-pointer"
          :title="closeTitle"
          @click.stop="emit('close')"
        >
          <IconDismiss12Regular />
        </button>
      </div>

      <!-- Normal tree mode -->
      <template v-if="!isSearching">
        <CommentaryTreeSectionNode
          v-for="node in tree"
          :key="node.label"
          :node="node"
          :scope-key="scopeKey"
          @toggle-item="toggleItem"
          @toggle-node="toggleNode"
          @navigate-to-book="scrollToBook"
        />
      </template>

      <!-- Search results mode: flat list with path subtitle -->
      <template v-else>
        <div
          v-for="result in searchResults"
          :key="`${result.item.bookId}::${result.item.sectionLabel}::${result.item.subSectionLabel}`"
          class="result-row"
          :class="{ unchecked: !result.item.isChecked }"
        >
          <button class="result-title-btn" @click="scrollToBook(result.item.bookId)">
            <span class="result-title">{{ result.item.bookTitle }}</span>
            <span class="result-path">{{ result.displayPath }}</span>
          </button>
          <button class="result-check-btn" @click="toggleItem(result.item)">
            <span class="check-mark">&#10003;</span>
          </button>
        </div>
        <div v-if="searchResults.length === 0" class="no-results">
          &#x5DC;&#x5D0; &#x5E0;&#x5DE;&#x5E6;&#x5D0;
        </div>
      </template>
    </div>

    <div class="panel-search">
      <div class="search-inner">
        <span v-for="(token, i) in treeState.tokens" :key="i" class="token-pill">
          {{ token }}
          <button class="pill-remove" @click.stop="removeToken(i)">
            <IconDismiss12Regular />
          </button>
        </span>
        <input
          ref="searchInputRef"
          v-model="treeState.searchQuery"
          type="text"
          class="search-input"
          :placeholder="treeState.tokens.length ? '' : placeholder"
          @keydown="onKeydown"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.filter-panel {
  display: flex;
  flex-direction: column;
  height: 100%;
  width: fit-content;
  min-width: 140px;
  max-width: 100%;
  background: var(--bg-secondary);
  direction: rtl;
}

.tree-scroll {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  overflow-x: hidden;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

.all-row {
  display: flex;
  align-items: center;
  gap: 4px;
  height: 28px;
  flex-shrink: 0;
  /* No end padding: the close button stretches to this row's end edge, like an
     expander does in a tree row. The label carries the inset instead. */
  padding-inline-start: 6px;
  cursor: pointer;
  font-size: 12px;
  font-weight: 600;
  color: var(--text-primary);
  border-bottom: 1px solid var(--border-color);
}

.all-row:hover { background: color-mix(in srgb, var(--text-primary) 8%, transparent); }

.check-col {
  width: 16px;
  height: 16px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  color: var(--accent-color);
}

.check-mark { display: none; }
.dash-mark  { display: none; }

.all-row.checked       .check-mark { display: block; }
.all-row.indeterminate .dash-mark  { display: block; }

.row-label { flex: 1; white-space: nowrap; padding-inline-end: 10px; }

/* Closes the whole tree. Styled as the tree's expander buttons (see
   CommentaryTreeSectionNode): same 26px full-height square cell, same secondary
   colour and 8% hover wash, so the panel's two chrome buttons read as one family.
   .row-label's flex:1 pushes it to the row's end edge. */
.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  flex-shrink: 0;
  align-self: stretch;
  color: var(--text-secondary);
  padding: 0;
  margin: 0;
  border-radius: 0;
}
/* 16%, not the expanders' 8%: unlike a tree row, .all-row washes itself at 8% on
   hover, so a matching 8% here would leave the button indistinguishable from it. */
.close-btn:hover  { background: color-mix(in srgb, var(--text-primary) 16%, transparent); }
.close-btn:active { transform: none !important; }
.close-btn :deep(svg) { width: 12px; height: 12px; }

.no-results {
  padding: 8px 10px;
  font-size: 11px;
  color: var(--text-secondary);
  text-align: center;
}

.result-row {
  display: flex;
  flex-direction: row-reverse;
  align-items: stretch;
  min-height: 44px;
  flex-shrink: 0;
}

.result-row:hover { background: color-mix(in srgb, var(--text-primary) 8%, transparent); }

.result-title-btn {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  justify-content: center;
  padding-block: 6px;
  padding-inline: 8px;
  gap: 2px;
  text-align: right;
  background: none;
  border: none;
  cursor: pointer;
  border-radius: 0;
}

.result-title-btn:hover { background: color-mix(in srgb, var(--text-primary) 8%, transparent); }

.result-check-btn {
  width: 28px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  color: var(--accent-color);
  padding: 0;
  background: none;
  border: none;
  cursor: pointer;
  border-radius: 0;
}

.result-check-btn:hover  { background: color-mix(in srgb, var(--text-primary) 8%, transparent); }
.result-check-btn:active { transform: none !important; }

.result-row .check-mark { display: none; }
.result-row:not(.unchecked) .check-mark { display: block; }

.result-title {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.result-path {
  font-size: 10px;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.panel-search {
  flex-shrink: 0;
  border-top: 1px solid var(--border-color);
  padding: 5px 6px 6px;
  box-sizing: border-box;
  background: var(--bg-secondary);
}

.search-inner {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 4px;
  padding: 4px 8px;
  border-radius: 999px;
  background: var(--input-bg);
  border: 1px solid var(--border-color);
  min-height: 26px;
  cursor: text;
}

.search-inner:focus-within { border-color: var(--accent-color); }

.token-pill {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  padding: 0 5px 0 4px;
  height: 18px;
  border-radius: 999px;
  background: color-mix(in srgb, var(--accent-color) 18%, transparent);
  color: var(--accent-color);
  font-size: 11px;
  white-space: nowrap;
  flex-shrink: 0;
}

.pill-remove {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 12px;
  height: 12px;
  border-radius: 50%;
  color: var(--accent-color);
  opacity: 0.7;
  padding: 0;
}

.pill-remove:hover {
  opacity: 1;
  background: color-mix(in srgb, var(--accent-color) 25%, transparent);
}

.search-input {
  flex: 1;
  min-width: 60px;
  width: 0;
  background: none;
  border: none;
  outline: none;
  font-size: 12px;
  color: var(--text-primary);
  padding: 0;
}

.search-input::placeholder { color: var(--text-secondary); }
</style>
