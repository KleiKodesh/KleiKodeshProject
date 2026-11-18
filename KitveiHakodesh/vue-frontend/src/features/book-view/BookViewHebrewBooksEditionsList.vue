<script setup lang="ts">
/**
 * The rows of a book's scanned editions, and what picking one does - ONE implementation
 * shared by the toolbar dropdown (BookViewHebrewBooksEditionsDropdown) and its collapsed row
 * in the toolbar's "more" flyout, so the two never drift apart. Top to bottom:
 *
 *  - the reader's own items (userEditionsStore): PDFs from their computer and HebrewBooks
 *    books they added, each with a trash button on hover that drops it from the list
 *    (the file itself is never touched);
 *  - Otzaria's scanned tractate (Talmud Bavli), when it is on this computer;
 *  - the catalog suggestions: one already on the computer (HebrewBooks folder or cache)
 *    always, the rest only while online - they download into the HebrewBooks folder;
 *  - "add a PDF from the computer" and "add from HebrewBooks".
 *
 * Every pick opens at the outline entry for the section the reader is in now - read at
 * click time, so it is wherever they scrolled to; a PDF whose bookmarks do not match opens
 * at its start (useBookEditionOpen). "Add from HebrewBooks" is asked of the host
 * (`add-hebrew-book`): its search window must outlive this list, which closes with the
 * dropdown. The host only closes itself, on `picked`.
 */
import { computed, inject, onMounted } from 'vue'
import { useOnline } from '@vueuse/core'
import { IconDelete16Regular } from '@iconify-prerendered/vue-fluent'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useHebrewBooksSideEditionStore } from '@/stores/hebrewBooksSideEditionStore'
import { useUserEditionsStore } from '@/stores/userEditionsStore'
import { pickLocalFilePath } from '@/webview-host/bridge'
import { useHebrewBooksSideEditionMode } from './useHebrewBooksSideEditionMode'
import { useBookEditionOpen, userItemAsEdition } from './useBookEditionOpen'
import type { HebrewBooksEdition, UserEdition } from '@/webview-host/queries.types'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const props = defineProps<{
  editions: HebrewBooksEdition[]
  talmudPdfPath: string | null
}>()

const emit = defineEmits<{ picked: []; 'add-hebrew-book': [] }>()

const paneId = inject<1 | 2>('paneId', 1)
const paneNavigation = usePaneNavigation()
const sideEditionStore = useHebrewBooksSideEditionStore()
const userEditionsStore = useUserEditionsStore()
const { isSideBySideAvailable, isToggleOn, side } = useHebrewBooksSideEditionMode(paneId)
const editionOpen = useBookEditionOpen(paneId)
const isOnline = useOnline()

const bookId = computed(() => paneNavigation.activeTab.bookId ?? 0)
const userEditions = computed(() => userEditionsStore.editionsOf(bookId.value))

/** Suggestions the reader can open now: on the computer, or downloadable while online. */
const visibleEditions = computed(() => props.editions.filter((e) => e.isOnDisk || isOnline.value))

onMounted(() => {
  void userEditionsStore.load(bookId.value)
})

function isShownSideBySide(edition: HebrewBooksEdition): boolean {
  return sideEditionStore.getShownEditionId(paneId) === edition.id
}

/** Author, place, year and page count - whichever of them the catalogue has. */
function detailsFor(edition: HebrewBooksEdition): string {
  return [
    edition.author,
    edition.printingPlace,
    edition.printingYear,
    edition.pages ? t('bookView.hebrewBooks.pages', { n: edition.pages }) : '',
  ]
    .map((part) => (part ?? '').trim())
    .filter(Boolean)
    .join(' · ')
}

/** Why a user item cannot open now, or '' when it can. */
function unavailableReason(item: UserEdition): string {
  if (item.kind === 'file') return item.fileExists ? '' : t('bookView.hebrewBooks.fileMissing')
  return item.isOnDisk || isOnline.value ? '' : t('bookView.hebrewBooks.notAvailableOffline')
}

function openHebrewBooksEdition(edition: HebrewBooksEdition) {
  emit('picked')
  editionOpen.openHebrewBooksEdition(edition)
}

function openLocalPdf(filePath: string, title: string) {
  emit('picked')
  editionOpen.openLocalPdf(filePath, title)
}

function onUserItem(item: UserEdition) {
  if (unavailableReason(item)) return
  if (item.kind === 'file' && item.filePath) openLocalPdf(item.filePath, item.title)
  else if (item.kind === 'hb' && item.hbId) openHebrewBooksEdition(userItemAsEdition(item))
}

function onTalmud() {
  if (props.talmudPdfPath) openLocalPdf(props.talmudPdfPath, paneNavigation.activeTab.title)
}

function onRemove(item: UserEdition) {
  void userEditionsStore.remove(bookId.value, item.id)
}

function stripExtension(fileName: string): string {
  const dot = fileName.lastIndexOf('.')
  return dot > 0 ? fileName.substring(0, dot) : fileName
}

async function onAddFile() {
  const picked = await pickLocalFilePath()
  if (!picked) return
  const title = stripExtension(picked.fileName)
  await userEditionsStore.addFile(bookId.value, picked.filePath, title)
  openLocalPdf(picked.filePath, title)
}

</script>

<template>
  <label v-if="isSideBySideAvailable" class="side-panel-toggle-row" @click.stop>
    <input v-model="isToggleOn" type="checkbox" />
    <span>{{ $t('bookView.hebrewBooks.showInSidePanel') }}</span>
  </label>
  <div
    v-if="isSideBySideAvailable && isToggleOn"
    class="side-panel-side-row"
    role="radiogroup"
    :aria-label="$t('bookView.hebrewBooks.sidePanelSide')"
    @click.stop
  >
    <span>{{ $t('bookView.hebrewBooks.sidePanelSide') }}</span>
    <label><input v-model="side" type="radio" value="right" />{{ $t('bookView.hebrewBooks.sidePanelRight') }}</label>
    <label><input v-model="side" type="radio" value="left" />{{ $t('bookView.hebrewBooks.sidePanelLeft') }}</label>
  </div>

  <template v-if="userEditions.length">
    <div class="section-label">{{ $t('bookView.hebrewBooks.userItems') }}</div>
    <div
      v-for="item in userEditions"
      :key="item.id"
      class="user-item"
    >
      <button dir="rtl"
        role="menuitem"
        class="edition-row"
        :class="{ 'edition-row-unavailable': !!unavailableReason(item) }"
        :aria-disabled="!!unavailableReason(item) || undefined"
        :title="unavailableReason(item) || item.filePath || item.title"
        @click="onUserItem(item)"
      >
        <span class="edition-title-line">
          <span class="edition-title">{{ item.title }}</span>
        </span>
        <span v-if="unavailableReason(item)" class="edition-details">{{ unavailableReason(item) }}</span>
      </button>
      <button
        class="user-item-remove"
        :title="$t('bookView.hebrewBooks.remove')"
        :aria-label="$t('bookView.hebrewBooks.remove')"
        @click.stop="onRemove(item)"
      >
        <IconDelete16Regular />
      </button>
    </div>
  </template>

  <button v-if="talmudPdfPath" dir="rtl" role="menuitem" class="edition-row" :title="talmudPdfPath" @click="onTalmud">
    <span class="edition-title-line">
      <span class="edition-title">{{ $t('bookView.hebrewBooks.otzariaTalmud') }}</span>
    </span>
  </button>

  <template v-if="visibleEditions.length">
    <div v-if="userEditions.length || talmudPdfPath" class="section-label">{{ $t('bookView.hebrewBooks.suggestions') }}</div>
    <button dir="rtl"
      v-for="edition in visibleEditions"
      :key="edition.id"
      role="menuitem"
      class="edition-row"
      :class="{ 'edition-row-shown': isShownSideBySide(edition) }"
      :aria-current="isShownSideBySide(edition) || undefined"
      :title="edition.title"
      @click="openHebrewBooksEdition(edition)"
    >
      <span class="edition-title-line">
        <span class="edition-title">{{ edition.title }}</span>
        <span v-if="edition.isBest" class="edition-tag edition-tag-best">{{ $t('bookView.hebrewBooks.recommended') }}</span>
        <span v-if="!edition.isCertain" class="edition-tag edition-tag-uncertain">{{ $t('bookView.hebrewBooks.possibleMatch') }}</span>
      </span>
      <span v-if="detailsFor(edition)" class="edition-details">{{ detailsFor(edition) }}</span>
    </button>
  </template>

  <div class="add-actions">
    <button dir="rtl" role="menuitem" class="edition-row add-row" @click.stop="onAddFile">
      {{ $t('bookView.hebrewBooks.addFile') }}
    </button>
    <button dir="rtl" role="menuitem" class="edition-row add-row" @click="emit('add-hebrew-book')">
      {{ $t('bookView.hebrewBooks.addFromHebrewBooks') }}
    </button>
  </div>

</template>

<style scoped>
.side-panel-toggle-row {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 10px;
  font-size: 12px;
  color: var(--text-primary);
  border-bottom: 1px solid var(--border-color);
  cursor: pointer;
  user-select: none;
}
.side-panel-side-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 4px 10px;
  font-size: 12px;
  color: var(--text-secondary);
  border-bottom: 1px solid var(--border-color);
  user-select: none;
}
.side-panel-side-row label {
  display: flex;
  align-items: center;
  gap: 4px;
  color: var(--text-primary);
  cursor: pointer;
}
.side-panel-side-row input,
.side-panel-toggle-row input {
  margin: 0;
  accent-color: var(--accent-color);
}
.section-label {
  padding: 4px 10px 2px;
  font-size: 10px;
  color: var(--text-secondary);
}
.edition-row {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 0;
  width: 100%;
  min-height: 28px;
  padding: 3px 10px;
  text-align: start;
  font-size: 12px;
  color: var(--text-primary);
  border-radius: 0;
}
.edition-row:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.edition-row-shown {
  box-shadow: inset -3px 0 0 var(--accent-color);
  background: color-mix(in srgb, var(--accent-color) 8%, transparent);
}
.edition-row:active {
  background: color-mix(in srgb, var(--text-primary) 10%, transparent);
}
.edition-row-unavailable {
  opacity: 0.55;
  cursor: default;
}

.user-item {
  position: relative;
  display: flex;
  align-items: stretch;
}
.user-item .edition-row {
  flex: 1 1 auto;
  min-width: 0;
  padding-inline-end: 30px;
}
.user-item-remove {
  position: absolute;
  inset-inline-end: 4px;
  top: 50%;
  transform: translateY(-50%);
  display: none;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  padding: 0;
  border-radius: 3px;
  color: var(--text-secondary);
}
.user-item:hover .user-item-remove,
.user-item-remove:focus-visible {
  display: flex;
}
.user-item-remove:hover {
  color: var(--status-danger);
  background: color-mix(in srgb, var(--status-danger) 10%, transparent);
}

.add-actions {
  border-top: 1px solid var(--border-color);
}
.add-row {
  justify-content: center;
  color: var(--text-secondary);
}

.edition-title-line {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
}
.edition-title {
  flex: 1 1 auto;
  min-width: 0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.edition-details {
  font-size: 11px;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.edition-tag {
  flex-shrink: 0;
  padding: 0 5px;
  border-radius: 3px;
  font-size: 10px;
  line-height: 16px;
}
.edition-tag-best {
  color: var(--accent-color);
  background: color-mix(in srgb, var(--accent-color) 12%, transparent);
}
.edition-tag-uncertain {
  color: var(--text-secondary);
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
</style>
