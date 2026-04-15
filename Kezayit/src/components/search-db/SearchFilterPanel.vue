<script setup lang="ts">
import { computed, ref, onMounted, nextTick, watch } from 'vue'
import { useDebounceFn, useIntervalFn } from '@vueuse/core'
import { IconMinimize20Regular, IconDismiss12Regular } from '@iconify-prerendered/vue-fluent'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { normalize } from '@/utils/normalizeText'
import LoadingAnimation from '@/components/common/LoadingAnimation.vue'
import SearchFilterNode from './SearchFilterNode.vue'
import SearchFilterBookList from './SearchFilterBookList.vue'
import type { CategoryNode, BookRow } from '@/components/books-fs/booksCategoryTree'

const props = defineProps<{
  checkedBookIds: Set<number>
  resultCounts: Map<number, number>
  hasSearched?: boolean
  atFilters: string[]
}>()
const emit = defineEmits<{
  toggleBook: [number]
  toggleCategory: [CategoryNode, boolean]
  checkAll: []
  uncheckAll: []
  close: []
  'update:atFilters': [string[]]
}>()

const booksStore = useBooksDataStore()

const bookListRef = ref<InstanceType<typeof SearchFilterBookList> | null>(null)
const searchInputRef = ref<HTMLInputElement | null>(null)
const inputText = ref('')
const filteredBooks = ref<BookRow[]>([])

onMounted(() => nextTick(() => searchInputRef.value?.focus()))

// ── Animated placeholder ──────────────────────────────────────────────────────

const PLACEHOLDERS = ['רש"י @ רמב"ם', 'בבלי ברכות', 'תוספתא @ תנ"ך תורה']
const placeholder = ref(PLACEHOLDERS[0]!)
let phraseIdx = 0, charIdx = 0, pauseTicks = 0

const { pause: pauseTyping, resume: resumeTyping } = useIntervalFn(() => {
  if (pauseTicks > 0) { pauseTicks--; return }
  const target = PLACEHOLDERS[phraseIdx]!
  if (charIdx < target.length) {
    placeholder.value = target.slice(0, ++charIdx)
  } else {
    pauseTicks = 12
    phraseIdx = (phraseIdx + 1) % PLACEHOLDERS.length
    charIdx = 0
  }
}, 80)

// Pause when user is typing or tokens are committed
watch([inputText, () => props.atFilters.length], ([text, count]) => {
  if (text || count) pauseTyping()
  else resumeTyping()
})

const total = computed(() => booksStore.allBooks.length)
const isAllChecked = computed(() => total.value > 0 && props.checkedBookIds.size === total.value)
const isIndet = computed(
  () => props.checkedBookIds.size > 0 && props.checkedBookIds.size < total.value,
)

// Show book list when there are committed tokens OR the current input is long enough
const activeQuery = computed(() => inputText.value.trim())
const isSearching = computed(() => props.atFilters.length > 0 || activeQuery.value.length >= 2)

// ── Book matching (same logic as useSearchFilters.matchBookIds but returns BookRow[]) ──

function toWords(raw: string): string[] {
  return normalize(raw.trim()).split(/\s+/).filter((w) => w.length > 0)
}

function matchBooks(q: string): BookRow[] {
  const words = toWords(q)
  if (!words.length) return []
  const exactWords = words.slice(0, -1)
  const prefixWord = words[words.length - 1]!
  return booksStore.allBooks.filter((b) => {
    const pathWords = b.searchWords ?? (b.searchPath ?? '').split(/\s+/)
    const exactOk = exactWords.every((qw) => pathWords.some((pw) => pw === qw))
    const prefixOk = pathWords.some((pw) => pw.includes(prefixWord))
    return exactOk && prefixOk
  })
}

// Union of all committed tokens + current input text (if long enough)
function computeFilteredBooks(tokens: string[], currentInput: string): BookRow[] {
  const allTokens = [
    ...tokens,
    ...(currentInput.trim().length >= 2 ? [currentInput.trim()] : []),
  ]
  if (!allTokens.length) return []
  const seen = new Set<number>()
  const result: BookRow[] = []
  for (const token of allTokens) {
    for (const book of matchBooks(token)) {
      if (!seen.has(book.id)) {
        seen.add(book.id)
        result.push(book)
      }
    }
  }
  return result
}

const runSearch = useDebounceFn(() => {
  filteredBooks.value = computeFilteredBooks(props.atFilters, inputText.value)
}, 150)

watch([() => props.atFilters, inputText], () => runSearch(), { immediate: true })

// ── Token management ──────────────────────────────────────────────────────────

function commitInput() {
  const text = inputText.value.trim()
  if (!text) return
  emit('update:atFilters', [...props.atFilters, text])
  inputText.value = ''
}

function removeToken(index: number) {
  const next = props.atFilters.filter((_, i) => i !== index)
  emit('update:atFilters', next)
  nextTick(() => searchInputRef.value?.focus())
}

function onInputKeydown(e: KeyboardEvent) {
  if (e.key === 'Enter' || e.key === '@') {
    e.preventDefault()
    commitInput()
    return
  }
  if (e.key === 'Backspace' && inputText.value === '' && props.atFilters.length > 0) {
    e.preventDefault()
    removeToken(props.atFilters.length - 1)
    return
  }
  if (e.key === 'ArrowDown' || e.key === 'Tab') {
    e.preventDefault()
    bookListRef.value?.focusList()
    return
  }
  if (e.key === 'ArrowUp') {
    e.preventDefault()
    bookListRef.value?.focusList()
    return
  }
  if (e.key === 'Escape') {
    e.preventDefault()
    emit('close')
  }
}
</script>

<template>
  <div class="panel" @keydown.esc.stop="emit('close')">
    <div class="panel-header">
      <div
        class="header-check"
        :class="{ checked: isAllChecked, indet: isIndet }"
        @click="isAllChecked ? emit('uncheckAll') : emit('checkAll')"
      >
        <span class="check-col">
          <span class="check-mark">✓</span>
          <span class="dash-mark">–</span>
        </span>
        <span class="panel-title">בחר הכל</span>
      </div>
      <button class="close-btn c-pointer hover-bg" title="סגור" @click.stop="emit('close')">
        <IconMinimize20Regular />
      </button>
    </div>

    <div class="panel-body">
      <LoadingAnimation v-if="booksStore.loading" />
      <template v-else>
        <SearchFilterBookList
          v-if="isSearching"
          ref="bookListRef"
          :books="filteredBooks"
          :checked-book-ids="checkedBookIds"
          :result-counts="resultCounts"
          :has-searched="hasSearched"
          @toggle-book="emit('toggleBook', $event)"
        />
        <div v-else class="tree-scroll">
          <SearchFilterNode
            v-for="cat in booksStore.ROOT.children"
            :key="cat.id"
            :category="cat"
            :checked-book-ids="checkedBookIds"
            :result-counts="resultCounts"
            :has-searched="hasSearched"
            @toggle-book="emit('toggleBook', $event)"
            @toggle-category="(c, v) => emit('toggleCategory', c, v)"
          />
        </div>
      </template>
    </div>

    <div class="panel-search">
      <div class="search-inner">
        <span
          v-for="(token, i) in atFilters"
          :key="i"
          class="token-pill"
        >
          {{ token }}
          <button class="pill-remove" @click.stop="removeToken(i)">
            <IconDismiss12Regular />
          </button>
        </span>
        <input
          ref="searchInputRef"
          v-model="inputText"
          type="text"
          name="filter-book-search"
          class="search-input"
          :placeholder="atFilters.length ? '' : placeholder"
          @keydown="onInputKeydown"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.panel {
  position: absolute;
  right: 0;
  top: 0;
  bottom: 0;
  z-index: 10;
  display: flex;
  flex-direction: column;
  min-width: 180px;
  max-width: 300px;
  background: var(--bg-secondary);
  border-left: 1px solid var(--border-color);
}
.panel-header {
  display: flex;
  align-items: center;
  height: 26px;
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
}
.header-check {
  display: flex;
  align-items: center;
  flex: 1;
  height: 26px;
  cursor: pointer;
  font-size: 12px;
  font-weight: 600;
  color: var(--text-primary);
}
.header-check:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.check-col {
  width: 28px;
  height: 26px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  color: var(--accent-color);
}
.check-mark { display: none; }
.dash-mark  { display: none; }
.header-check.checked .check-mark { display: block; }
.header-check.indet   .dash-mark  { display: block; }
.panel-title { flex: 1; }
.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  flex-shrink: 0;
  border-radius: 0;
  color: var(--text-secondary);
}
.panel-body {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}
.tree-scroll {
  flex: 1;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}
.panel-search {
  padding: 5px 6px 6px;
  border-top: 1px solid var(--border-color);
  flex-shrink: 0;
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
.search-inner:focus-within {
  border-color: var(--accent-color);
}
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
  background: none;
  border: none;
  outline: none;
  font-size: 12px;
  color: var(--text-primary);
  direction: rtl;
  padding: 0;
}
.search-input::placeholder {
  color: var(--text-secondary);
}
</style>
