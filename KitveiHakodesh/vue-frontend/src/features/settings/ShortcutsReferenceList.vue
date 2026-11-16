<template>
  <div class="shortcuts-list">
    <template v-for="group in groups" :key="group.id">
      <div class="shortcuts-group-label">{{ group.title }}</div>
      <div v-for="row in group.rows" :key="row.id" class="shortcut-block">
        <div class="shortcut-row" :class="{ capturing: capturingId === row.id }">
          <div class="shortcut-keys">
            <template v-if="capturingId === row.id">
              <span class="capture-prompt">{{ $t('settings.shortcutsEdit.pressCombo') }}</span>
            </template>
            <template v-else-if="row.combos.length === 0">
              <span class="no-shortcut">{{ $t('settings.shortcutsEdit.none') }}</span>
            </template>
            <template v-else>
              <template v-for="(combo, ci) in row.combos" :key="combo">
                <span v-if="ci > 0" class="kbd-plus">/</span>
                <template v-for="(key, ki) in keysOf(combo)" :key="ki">
                  <span v-if="ki > 0" class="kbd-plus">+</span><kbd>{{ key }}</kbd>
                </template>
              </template>
            </template>
          </div>
          <span class="shortcut-description">{{ row.text }}</span>
          <div v-if="editable" class="shortcut-actions">
            <span v-if="row.fixed" class="fixed-tag" :title="$t('settings.shortcutsEdit.fixedTip')">{{ $t('settings.shortcutsEdit.fixed') }}</span>
            <template v-else>
              <button v-if="capturingId === row.id" type="button" class="row-btn" @click="cancelCapture">{{ $t('settings.shortcutsEdit.cancel') }}</button>
              <button v-else type="button" class="row-btn" @click="beginCapture(row.id)">{{ $t('settings.shortcutsEdit.change') }}</button>
              <button v-if="row.overridden && capturingId !== row.id" type="button" class="row-btn" @click="restore(row.id)">{{ $t('settings.shortcutsEdit.restore') }}</button>
              <button v-if="row.personal && capturingId !== row.id" type="button" class="row-btn" @click="removePersonal(row.id)">{{ $t('settings.shortcutsEdit.remove') }}</button>
            </template>
          </div>
        </div>
        <div v-if="editable && capturingId === row.id" class="row-note">{{ $t('settings.shortcutsEdit.captureHint') }}</div>
        <div v-if="row.unreachable" class="row-note warn">{{ $t('settings.shortcutsEdit.unreachableInWord') }}</div>
        <div v-if="editable && notice?.id === row.id" class="row-note" :class="notice.kind">
          <span>{{ notice.text }}</span>
          <template v-if="notice.pending">
            <button type="button" class="row-btn" @click="confirmReplace">{{ $t('settings.shortcutsEdit.replace') }}</button>
            <button type="button" class="row-btn" @click="notice = null">{{ $t('settings.shortcutsEdit.cancel') }}</button>
          </template>
        </div>
      </div>
      <!-- Personal group: the "add" control sits under its rows. -->
      <div v-if="editable && group.id === 'personal'" class="add-book">
        <button v-if="!adding" type="button" class="row-btn" @click="startAdding">{{ $t('settings.shortcutsEdit.addBook') }}</button>
        <template v-else>
          <div class="add-book-bar">
            <input
              ref="queryInput"
              v-model="query"
              class="add-book-input"
              type="search"
              data-ctrlf-enabled
              :placeholder="$t('settings.shortcutsEdit.bookPlaceholder')"
              @keydown.escape.stop="stopAdding"
              @keydown.enter.prevent="results[0] && pickBook(results[0])"
            />
            <button type="button" class="row-btn" @click="stopAdding">{{ $t('settings.shortcutsEdit.cancel') }}</button>
          </div>
          <div v-if="searchState === 'searching'" class="row-note">{{ $t('settings.shortcutsEdit.bookSearching') }}</div>
          <div v-else-if="searchState === 'none'" class="row-note">{{ $t('settings.shortcutsEdit.bookNotFound') }}</div>
          <div v-else-if="searchState === 'unavailable'" class="row-note error">{{ $t('settings.shortcutsEdit.bookSearchUnavailable') }}</div>
          <button v-for="book in results" :key="book.id" type="button" class="book-result" @click="pickBook(book)">{{ book.title }}</button>
        </template>
      </div>
    </template>
    <div v-if="editable" class="shortcuts-footer">
      <div class="shortcuts-hint">{{ $t('settings.shortcutsEdit.hint') }}</div>
      <button type="button" class="settings-btn" :disabled="!store.hasOverrides" @click="restoreAll">{{ $t('settings.shortcutsEdit.restoreAll') }}</button>
    </div>
  </div>
</template>

<script setup lang="ts">
/**
 * The keyboard shortcuts list, drawn from `src/shortcuts/shortcutRegistry.ts` with the user's
 * own combinations from the shortcuts store, and the user's personal shortcuts above it.
 *
 * Read-only in the setup wizard; `editable` in settings, where each changeable row gets
 * "change" (press the new combination - recorded by `shortcuts/shortcutCapture.ts`, which sees
 * the press before any shortcut does) and, once changed, "restore". A combination already in
 * use where this one acts asks before taking it over; a fixed one, a reserved one, or one
 * without Ctrl/Alt (other than F1-F12) is refused with the reason.
 *
 * Personal shortcuts: "add a shortcut to a book" searches the catalog by title (the catalog
 * index's own title search, `searchBookTitleIds`), and picking a book creates the shortcut and
 * goes straight to recording its keys. Each personal row can be changed or removed.
 */
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useDebounceFn, useWindowSize } from '@vueuse/core'
import { isVstoEnvironment } from '@/webview-host/bridge'
import { isPersonalId, useShortcutsStore, type ShortcutConflict } from '@/shortcuts/shortcutsStore'
import { SHORTCUTS, SHORTCUT_GROUPS, SHORTCUTS_BY_ID } from '@/shortcuts/shortcutRegistry'
import { comboKeys, comboProblem, comboText, UNREACHABLE_IN_WORD, type Combo } from '@/shortcuts/shortcutCombo'
import { capturingId, startCapture, stopCapture } from '@/shortcuts/shortcutCapture'
import { searchBookTitleIds } from '@/features/book-catalog/bookTitleSearch'
import { useBooksDataStore } from '@/stores/booksDataStore'

const props = defineProps<{ editable?: boolean }>()

const { t } = useI18n()
const store = useShortcutsStore()
const booksData = useBooksDataStore()

const arrows = computed(() => ({
  left: t('settings.shortcutsList.leftArrow'),
  right: t('settings.shortcutsList.rightArrow'),
  up: t('settings.shortcutsList.upArrow'),
  down: t('settings.shortcutsList.downArrow'),
}))
const keysOf = (combo: Combo) => comboKeys(combo, arrows.value)
const textOf = (combo: Combo) => comboText(combo, arrows.value)
const nameOf = (id: string) => {
  if (isPersonalId(id)) {
    const p = store.personalById(id)
    return p ? t('settings.shortcutsEdit.openBook', { title: p.title }) : id
  }
  const def = SHORTCUTS_BY_ID.get(id)
  return def ? t('settings.shortcutsList.' + def.label) : id
}

interface Row {
  id: string
  text: string
  combos: readonly Combo[]
  fixed: boolean
  overridden: boolean
  personal: boolean
  unreachable: boolean
}

const unreachable = (combos: readonly Combo[]) => isVstoEnvironment && combos.some((c) => UNREACHABLE_IN_WORD.has(c))

// Re-evaluate the width-dependent rows (side commentary) when the window is resized.
const { width } = useWindowSize()

const groups = computed(() => {
  void width.value
  const out: { id: string; title: string; rows: Row[] }[] = []
  // Personal first: it is the user's own. Shown in settings even when empty, for its "add".
  if (props.editable || store.personal.length > 0) {
    out.push({
      id: 'personal',
      title: t('settings.shortcutsEdit.personalGroup'),
      rows: store.personal.map((p) => {
        const combos = p.combo ? [p.combo] : []
        return { id: p.id, text: nameOf(p.id), combos, fixed: false, overridden: false, personal: true, unreachable: unreachable(combos) }
      }),
    })
  }
  for (const g of SHORTCUT_GROUPS) {
    if (g.shown && !g.shown()) continue
    const rows = SHORTCUTS.filter((d) => d.group === g.id && d.available !== false && (!d.shown || d.shown())).map((def): Row => {
      const combos = store.combosFor(def.id)
      return {
        id: def.id, text: t('settings.shortcutsList.' + def.label), combos, fixed: !!def.fixed,
        overridden: store.isOverridden(def.id), personal: false, unreachable: unreachable(combos),
      }
    })
    if (rows.length > 0) out.push({ id: g.id, title: t('settings.shortcutsList.' + g.label), rows })
  }
  return out
})

interface Notice {
  id: string
  kind: 'error' | 'info'
  text: string
  /** A combination waiting for "replace". */
  pending?: Combo
}
const notice = ref<Notice | null>(null)

function beginCapture(id: string) {
  notice.value = null
  startCapture(id, {
    onCombo: (combo) => tryAssign(id, combo),
    onClear: () => store.assign(id, null),
    onCancel: () => {},
  })
}

function cancelCapture() {
  stopCapture()
}

function conflictText(combo: Combo, conflicts: ShortcutConflict[]) {
  const names = conflicts.map((c) => '"' + nameOf(c.def.id) + '"').join(', ')
  return t('settings.shortcutsEdit.conflict', { combo: textOf(combo), action: names })
}

function tryAssign(id: string, combo: Combo) {
  const problem = comboProblem(combo)
  if (problem) {
    notice.value = { id, kind: 'error', text: t('settings.shortcutsEdit.problem.' + problem, { combo: textOf(combo) }) }
    return
  }
  if (store.combosFor(id).length === 1 && store.combosFor(id)[0] === combo) return
  const conflicts = store.conflictsFor(id, combo)
  const fixed = conflicts.find((c) => c.def.fixed)
  if (fixed) {
    notice.value = { id, kind: 'error', text: t('settings.shortcutsEdit.conflictFixed', { combo: textOf(combo), action: '"' + nameOf(fixed.def.id) + '"' }) }
    return
  }
  if (conflicts.length > 0) {
    notice.value = { id, kind: 'error', text: conflictText(combo, conflicts) + ' ' + t('settings.shortcutsEdit.replaceQuestion'), pending: combo }
    return
  }
  store.assign(id, combo)
}

function confirmReplace() {
  const n = notice.value
  if (!n?.pending) return
  store.assign(n.id, n.pending)
  notice.value = null
}

function restore(id: string) {
  const first = store.reset(id)[0]
  notice.value = first
    ? { id, kind: 'info', text: t('settings.shortcutsEdit.restoreTaken', { combo: textOf(first.combo), action: '"' + nameOf(first.def.id) + '"' }) }
    : null
}

function restoreAll() {
  stopCapture()
  const first = store.resetAll()[0]
  notice.value = first
    ? { id: first.id, kind: 'info', text: t('settings.shortcutsEdit.personalKeyCleared') }
    : null
}

// ── Personal shortcuts: add / remove ─────────────────────────────────────────

const adding = ref(false)
const query = ref('')
const queryInput = ref<HTMLInputElement[] | HTMLInputElement | null>(null)
const results = ref<{ id: number; title: string }[]>([])
const searchState = ref<'idle' | 'searching' | 'none' | 'unavailable'>('idle')
const MAX_RESULTS = 8
let searchSeq = 0

function startAdding() {
  stopCapture()
  notice.value = null
  adding.value = true
  query.value = ''
  results.value = []
  searchState.value = 'idle'
  void booksData.ensureLoaded()
  void nextTick(() => {
    const el = Array.isArray(queryInput.value) ? queryInput.value[0] : queryInput.value
    el?.focus()
  })
}

function stopAdding() {
  adding.value = false
  searchSeq++
}

const runSearch = useDebounceFn(async (q: string) => {
  const seq = ++searchSeq
  if (q.trim().length < 2) {
    results.value = []
    searchState.value = 'idle'
    return
  }
  searchState.value = 'searching'
  const ids = await searchBookTitleIds(q.trim(), () => seq !== searchSeq)
  if (seq !== searchSeq) return
  if (ids === null) {
    results.value = []
    searchState.value = 'unavailable'
    return
  }
  await booksData.ensureLoaded()
  if (seq !== searchSeq) return
  const map = booksData.allBooksMap
  results.value = ids.slice(0, MAX_RESULTS).flatMap((id) => {
    const book = map.get(id)
    return book ? [{ id, title: book.title }] : []
  })
  searchState.value = results.value.length > 0 ? 'idle' : 'none'
}, 250)

watch(query, (q) => { void runSearch(q) })

function pickBook(book: { id: number; title: string }) {
  stopAdding()
  const id = store.addPersonalBook(book.id, book.title)
  beginCapture(id)
}

function removePersonal(id: string) {
  if (capturingId.value === id) stopCapture()
  if (notice.value?.id === id) notice.value = null
  store.removePersonal(id)
}

onBeforeUnmount(() => {
  if (props.editable) stopCapture()
})
</script>

<style scoped>
.shortcuts-list {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.shortcuts-group-label {
  font-size: 11px;
  font-weight: 600;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  padding: 10px 0 4px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 60%, transparent);
  margin-bottom: 4px;
}

.shortcuts-group-label:first-child {
  padding-top: 0;
}

.shortcut-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  min-height: 30px;
  padding: 0 4px;
  border-radius: 4px;
}

.shortcut-row.capturing {
  background: color-mix(in srgb, var(--accent-color, var(--text-primary)) 12%, transparent);
}

.shortcut-keys {
  display: flex;
  align-items: center;
  gap: 3px;
  flex-shrink: 0;
  flex-wrap: wrap;
}

.shortcut-description {
  font-size: 13px;
  color: var(--text-primary);
  text-align: start;
  flex: 1;
  min-width: 0;
}

.shortcut-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
}

.row-btn {
  height: 24px;
  padding: 0 8px;
  font-size: 12px;
  color: var(--text-primary);
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  white-space: nowrap;
  cursor: pointer;
}

.row-btn:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

.fixed-tag {
  font-size: 11px;
  color: var(--text-secondary);
  padding: 0 6px;
}

.capture-prompt {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-primary);
}

.no-shortcut {
  font-size: 12px;
  color: var(--text-secondary);
}

.row-note {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
  font-size: 12px;
  color: var(--text-secondary);
  padding: 0 4px 6px;
}

.row-note.error,
.row-note.warn {
  color: var(--error-color, #c0392b);
}

.shortcuts-footer {
  padding-top: 12px;
}

.shortcuts-hint {
  font-size: 12px;
  color: var(--text-secondary);
}

kbd {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 26px;
  height: 22px;
  padding: 0 6px;
  font-family: 'Segoe UI Variable', 'Segoe UI', system-ui, sans-serif;
  font-size: 11px;
  font-weight: 600;
  color: var(--text-primary);
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 1px 0 var(--border-color);
  white-space: nowrap;
  direction: ltr;
}

.kbd-plus {
  font-size: 11px;
  color: var(--text-secondary);
  line-height: 1;
}
.add-book {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
  padding: 2px 4px 8px;
}

.add-book-bar {
  display: flex;
  align-items: center;
  gap: 6px;
  width: 100%;
}

.add-book-input {
  flex: 1;
  min-width: 0;
  height: 26px;
  padding: 0 8px;
  font-size: 13px;
  color: var(--text-primary);
  background: var(--bg-primary, transparent);
  border: 1px solid var(--border-color);
  border-radius: 4px;
}

.book-result {
  width: 100%;
  text-align: start;
  padding: 4px 8px;
  font-size: 13px;
  color: var(--text-primary);
  background: transparent;
  border: none;
  border-radius: 4px;
  cursor: pointer;
}

.book-result:hover,
.book-result:focus-visible {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
</style>
