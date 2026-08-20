<script setup lang="ts">
/**
 * Word-link marker visibility dropdown in the BookView toolbar: one checkbox per
 * marker commentary of the current book (fetched lazily on first open per book),
 * plus an all/none master row. Hidden commentaries persist globally
 * (settingsStore.hiddenWordLinkMarkerBookIds) — hiding one hides its markers in
 * every book. The hiding itself is a stylesheet injected by the settings store,
 * so toggling never re-splices already-rendered lines.
 */
import { ref, computed, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { IconLink20Regular, IconLinkDismiss20Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { getWordLinkTargetsForBook, wordLinkAnchorsSupported } from '@/webview-host/seforimApi'

const props = defineProps<{ bookId: number | undefined }>()

const settingsStore = useSettingsStore()
const booksDataStore = useBooksDataStore()
const bookViewStore = useBookViewStore()
const { toolbarPosition } = storeToRefs(bookViewStore)

// ── Open / close (BookViewRelatedBooksDropdown pattern) ─────────────────────

const isOpen = ref(false)
const dropdownRef = ref<HTMLElement | null>(null)
const toggleButtonRef = ref<HTMLElement | null>(null)

const { justClosed } = useDropdownClose(dropdownRef, () => (isOpen.value = false), {
  toggleButton: toggleButtonRef,
})

function toggleOpen() {
  if (justClosed.value) return
  isOpen.value = !isOpen.value
  if (isOpen.value) void load()
}

watch(
  () => props.bookId,
  () => {
    isOpen.value = false
  },
)

// ── The book's marker commentaries — lazy, once per book ────────────────────

interface MarkerCommentary {
  bookId: number
  title: string
}

const commentaries = ref<MarkerCommentary[]>([])
const loaded = ref(false)
const loadedForBookId = ref<number | null>(null)

async function load() {
  const bookId = props.bookId
  if (bookId == null) return
  if (loaded.value && loadedForBookId.value === bookId) return
  loaded.value = false
  let targets: Awaited<ReturnType<typeof getWordLinkTargetsForBook>> = null
  try {
    targets = await getWordLinkTargetsForBook(bookId)
  } catch {
    targets = null // dev service transport error — same retry treatment as a null result
  }
  // The tab's book switched while the fetch ran — this result belongs to the old book.
  if (props.bookId !== bookId) return
  const ids = [...new Set((targets ?? []).map((t) => t.targetBookId))]
  commentaries.value = ids
    .map((id) => ({
      bookId: id,
      title: booksDataStore.allBooksMap.get(id)?.title ?? `#${id}`,
    }))
    .sort((a, b) => a.title.localeCompare(b.title, 'he'))
  // null = transient failure: render what we have, but leave the book unstamped so
  // the next open refetches instead of trusting an empty answer.
  loadedForBookId.value = targets == null ? null : bookId
  loaded.value = true
}

// ── Checkbox state ───────────────────────────────────────────────────────────

const hiddenSet = computed(() => new Set(settingsStore.hiddenWordLinkMarkerBookIds))
const anyHiddenGlobally = computed(() => settingsStore.hiddenWordLinkMarkerBookIds.length > 0)

const allChecked = computed(() => commentaries.value.every((c) => !hiddenSet.value.has(c.bookId)))
const someChecked = computed(() => commentaries.value.some((c) => !hiddenSet.value.has(c.bookId)))

function setHidden(bookIds: number[], hidden: boolean) {
  const next = new Set(settingsStore.hiddenWordLinkMarkerBookIds)
  for (const id of bookIds) {
    if (hidden) next.add(id)
    else next.delete(id)
  }
  // A new array, so the store's persist watcher fires.
  settingsStore.hiddenWordLinkMarkerBookIds = [...next]
}

function toggleOne(c: MarkerCommentary) {
  setHidden([c.bookId], !hiddenSet.value.has(c.bookId))
}

function toggleAll() {
  // Shared with the Ctrl+U shortcut — all visible → hide all listed; else show all.
  settingsStore.toggleWordLinkMarkers(commentaries.value.map((c) => c.bookId))
}
</script>

<template>
  <!-- Absent entirely (not just disabled) until the DB is known to support word-link
       anchors — schema-v1 users never see a control for a subsystem they don't have. -->
  <div v-if="wordLinkAnchorsSupported" class="wl-markers-wrapper">
    <button
      ref="toggleButtonRef"
      :class="{ active: isOpen || anyHiddenGlobally }"
      title="ציוני מפרשים (Ctrl+U)"
      @click="toggleOpen"
    >
      <IconLinkDismiss20Regular v-if="anyHiddenGlobally" />
      <IconLink20Regular v-else />
    </button>

    <div v-if="isOpen" ref="dropdownRef" class="wl-markers-dropdown" :class="`dropdown-${toolbarPosition}`">
      <div v-if="!loaded" class="state-message">טוען...</div>
      <div v-else-if="commentaries.length === 0" class="state-message">אין ציוני מפרשים</div>
      <template v-else>
        <label class="marker-row all-row">
          <input
            type="checkbox"
            :checked="allChecked"
            :indeterminate="!allChecked && someChecked"
            @change="toggleAll"
          />
          <span>בחר הכל</span>
        </label>
        <label v-for="c in commentaries" :key="c.bookId" class="marker-row">
          <input type="checkbox" :checked="!hiddenSet.has(c.bookId)" @change="toggleOne(c)" />
          <span>{{ c.title }}</span>
        </label>
      </template>
    </div>
  </div>
</template>

<style scoped>
.wl-markers-wrapper {
  position: relative;
}

/* ── Toggle button — mirrors the toolbar's button sizing ── */
button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--toolbar-button-size);
  height: var(--toolbar-button-size);
  padding: 6px;
  border-radius: 4px;
  flex-shrink: 0;
}
button.active {
  color: var(--accent-color);
}

/* ── Dropdown panel (BookViewRelatedBooksDropdown look) ── */
.wl-markers-dropdown {
  position: absolute;
  min-width: 180px;
  max-width: 280px;
  max-height: 320px;
  overflow-y: auto;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25);
  z-index: 100;
  padding: 4px 0;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

.dropdown-top {
  top: calc(100% + 4px);
  right: 0;
}
.dropdown-bottom {
  bottom: calc(100% + 4px);
  right: 0;
}
.dropdown-left {
  top: 0;
  left: calc(100% + 4px);
}
.dropdown-right {
  top: 0;
  right: calc(100% + 4px);
}

.state-message {
  padding: 8px 12px;
  font-size: 12px;
  color: var(--text-secondary);
}

/* ── Checkbox rows ── */
.marker-row {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 32px;
  padding: 0 12px;
  font-size: 12px;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  cursor: pointer;
}
.marker-row:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.marker-row input {
  accent-color: var(--accent-color);
  cursor: pointer;
}
.all-row {
  border-bottom: 1px solid var(--border-color);
  margin-bottom: 2px;
}
</style>
