<script setup lang="ts">
/**
 * Word-link marker visibility dropdown in the BookView toolbar: one checkbox per
 * marker commentary of the current book, plus an all/none master row. Hidden
 * commentaries persist globally (settingsStore.hiddenWordLinkMarkerBookIds) —
 * hiding one hides its markers in every book. The hiding itself is a stylesheet
 * injected by the settings store, so toggling never re-splices rendered lines.
 *
 * The control exists only where it does something: most books cite nothing with a
 * marker, and a dropdown that can only say "none" is worse than no button. Deciding
 * that needs the list, so it is fetched once per book in the background after mount
 * — one indexed query, off the render path, the same one an open would have run.
 */
import { ref, computed, watch, onMounted, onScopeDispose } from 'vue'
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
    // Clear before refetching: an empty list hides the button, which is the honest
    // state for a book whose citations are not known yet — showing the previous
    // book's commentaries for a moment would not be.
    // A retry armed for the previous book would re-enter load() reading the NEW bookId,
    // duplicating the query below (and one live chain per switch when switching fast).
    cancelRetry()
    // Release the shared promise too: it belongs to the OLD book, and handing it to the
    // load below would make that load resolve on a query that bails on its own
    // book-switched guard - leaving the new book's list never fetched at all.
    inFlight = null
    isOpen.value = false
    loaded.value = false
    loadedForBookId.value = null
    commentaries.value = []
    void load()
  },
)

onMounted(() => void load())
onScopeDispose(cancelRetry)

// ── The book's marker commentaries — lazy, once per book ────────────────────

interface MarkerCommentary {
  bookId: number
  title: string
}

const commentaries = ref<MarkerCommentary[]>([])
// Whether this control is on the toolbar at all. The toolbar has to know, because its
// overflow arithmetic charges for every button it renders, and this one decides for itself
// (below) whether it renders - out of a list it fetches, which no caller can predict.
const emit = defineEmits<{ 'presence-change': [present: boolean] }>()

const isPresent = computed(() => !!wordLinkAnchorsSupported.value && commentaries.value.length > 0)
watch(isPresent, (present) => emit('presence-change', present), { immediate: true })
const loaded = ref(false)
const loadedForBookId = ref<number | null>(null)

/**
 * A transient failure now has to be retried from here. The button is absent until
 * the list arrives, so there is no longer a re-open to piggyback a retry on — and
 * without one, a single hiccup would silently cost the reader the control for the
 * rest of the session. Bounded, because a DB that is still failing after a few
 * seconds is not going to be fixed by asking again.
 */
const RETRY_DELAYS_MS = [400, 1500, 4000]

/** The armed retry, so a book switch or teardown can disarm it. */
let retryTimer: ReturnType<typeof setTimeout> | null = null
/** The load in flight, so two callers share one query instead of both issuing it. */
let inFlight: Promise<void> | null = null

function cancelRetry() {
  if (retryTimer !== null) {
    clearTimeout(retryTimer)
    retryTimer = null
  }
}

/**
 * One load at a time. onMounted's background load and a fast toggleOpen both pass the
 * loaded/loadedForBookId guard (nothing has resolved yet), so without this they issue the
 * same query twice for the same book.
 */
function load(attempt = 0): Promise<void> {
  inFlight ??= loadOnce(attempt).finally(() => {
    inFlight = null
  })
  return inFlight
}

async function loadOnce(attempt = 0) {
  const bookId = props.bookId
  if (bookId == null) return
  if (loaded.value && loadedForBookId.value === bookId) return
  let targets: Awaited<ReturnType<typeof getWordLinkTargetsForBook>> = null
  try {
    targets = await getWordLinkTargetsForBook(bookId)
  } catch {
    targets = null // dev service transport error — same treatment as a null result
  }
  // The tab's book switched while the fetch ran — this result belongs to the old book.
  if (props.bookId !== bookId) return
  if (targets == null) {
    // Leave the book unstamped so a later attempt refetches rather than trusting
    // an empty answer, and schedule that attempt.
    loadedForBookId.value = null
    const delay = RETRY_DELAYS_MS[attempt]
    if (delay != null) {
      cancelRetry()
      retryTimer = setTimeout(() => {
        retryTimer = null
        void load(attempt + 1)
      }, delay)
    }
    return
  }
  const ids = [...new Set(targets.map((t) => t.targetBookId))]
  commentaries.value = ids
    .map((id) => ({
      bookId: id,
      title: booksDataStore.allBooksMap.get(id)?.title ?? `#${id}`,
    }))
    .sort((a, b) => a.title.localeCompare(b.title, 'he'))
  loadedForBookId.value = bookId
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
  <!-- Absent entirely (not just disabled) unless this book actually has marker
       citations to filter: schema-v1 users never see a control for a subsystem they
       don't have, and neither does anyone reading a book that cites nothing. -->
  <div v-if="isPresent" class="wl-markers-wrapper">
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
      <!-- Only reachable while a retry after a failed load is in flight — the button
           itself does not exist until the list has arrived and is non-empty. -->
      <div v-if="!loaded" class="state-message">טוען...</div>
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
