<script setup lang="ts">
/**
 * "Add from HebrewBooks" window of the book view's scanned-editions dropdown: the same
 * catalog search the HebrewBooks page runs (searchHbCatalog), shown with that page's own row
 * (HebrewBooksListItem, details only). Picking a result emits it - the caller saves it as a user item of
 * this book. Starts searching the book's title.
 */
import { ref, onMounted, nextTick } from 'vue'
import { useDebounceFn, useEventListener } from '@vueuse/core'
import { searchHbCatalog, type HebrewBook } from '@/features/hebrewbooks/hebrewBooksCatalog'
import HebrewBooksListItem from '@/features/hebrewbooks/HebrewBooksListItem.vue'
import { useSettingsStore } from '@/stores/settingsStore'

const props = defineProps<{ initialTerm: string }>()
const emit = defineEmits<{ pick: [book: HebrewBook]; close: [] }>()

const settings = useSettingsStore()
const term = ref(props.initialTerm)
const results = ref<HebrewBook[]>([])
const isSearching = ref(false)
const inputRef = ref<HTMLInputElement | null>(null)

// A later search supersedes an earlier one still running.
let generation = 0
const runSearch = useDebounceFn(async (value: string) => {
  const mine = ++generation
  if (!value.trim()) {
    results.value = []
    isSearching.value = false
    return
  }
  isSearching.value = true
  const books = await searchHbCatalog(value, settings.hebrewBooksLocalFolder || undefined)
  if (mine !== generation) return
  results.value = books
  isSearching.value = false
}, 200)

function onInput() {
  runSearch(term.value)
}

useEventListener('keydown', (e: KeyboardEvent) => {
  if (e.key === 'Escape') {
    e.preventDefault()
    emit('close')
  }
})

onMounted(async () => {
  runSearch(term.value)
  await nextTick()
  inputRef.value?.select()
})
</script>

<template>
  <Teleport to="body">
    <div class="hb-add-backdrop" @click.self="emit('close')">
      <div class="hb-add-dialog" dir="rtl" role="dialog" :aria-label="$t('bookView.hebrewBooks.addFromHebrewBooks')">
        <p class="hb-add-title">{{ $t('bookView.hebrewBooks.addFromHebrewBooks') }}</p>
        <input
          ref="inputRef"
          v-model="term"
          type="search"
          class="hb-add-input"
          :placeholder="$t('bookView.hebrewBooks.searchPlaceholder')"
          @input="onInput"
        />
        <div class="hb-add-results">
          <HebrewBooksListItem
            v-for="book in results"
            :key="book.id"
            :book="book"
            hide-actions
            @book-clicked="emit('pick', book)"
          />
          <p v-if="!isSearching && term.trim() && !results.length" class="hb-add-empty">
            {{ $t('bookView.hebrewBooks.noResults') }}
          </p>
        </div>
        <div class="hb-add-actions">
          <button class="hb-add-cancel" @click="emit('close')">{{ $t('components.cancel') }}</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.hb-add-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
}
.hb-add-dialog {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 16px 16px 12px;
  width: min(560px, calc(100vw - 32px));
  max-height: min(640px, calc(100vh - 32px));
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.hb-add-title {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
}
.hb-add-input {
  height: 30px;
  padding: 0 8px;
  font-size: 13px;
  color: var(--text-primary);
  background: var(--control-bg);
  border: 1px solid var(--border-color);
  border-radius: 4px;
}
.hb-add-results {
  flex: 1 1 auto;
  min-height: 120px;
  overflow-y: auto;
  scrollbar-width: thin;
  border: 1px solid var(--border-color);
  border-radius: 4px;
}
.hb-add-empty {
  margin: 0;
  padding: 10px;
  font-size: 12px;
  color: var(--text-secondary);
}
.hb-add-actions {
  display: flex;
  justify-content: flex-end;
}
.hb-add-cancel {
  height: 30px;
  padding: 0 14px;
  font-size: 12px;
  border: 1px solid var(--border-color);
  background: var(--control-bg);
  color: var(--text-primary);
  border-radius: 4px;
}
.hb-add-cancel:hover {
  background: var(--control-bg-hover);
}
</style>
