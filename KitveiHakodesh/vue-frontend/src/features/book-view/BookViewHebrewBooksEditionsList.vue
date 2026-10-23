<script setup lang="ts">
/**
 * The rows of a book's HebrewBooks scanned editions, and what picking one does - ONE
 * implementation shared by the toolbar dropdown (BookViewHebrewBooksEditionsDropdown) and
 * its collapsed row in the toolbar's "more" flyout, so the two never drift apart.
 *
 * Picking opens the edition at the outline entry for the section the reader is in now - read
 * at click time, so it is wherever they scrolled to. Where side by side is available (see
 * useHebrewBooksSideEditionMode) a toggle row on top chooses between showing it in the
 * book view's left side panel and opening a new tab; the edition on screen in the side
 * panel is marked. The host only closes itself, on `picked`.
 */
import { inject } from 'vue'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useHebrewBooks } from '@/features/hebrewbooks/useHebrewBooks'
import { useHebrewBooksSideEditionStore } from '@/stores/hebrewBooksSideEditionStore'
import { useHebrewBooksSideEditionMode } from './useHebrewBooksSideEditionMode'
import type { HebrewBooksEdition } from '@/webview-host/queries.types'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

defineProps<{
  editions: HebrewBooksEdition[]
}>()

const emit = defineEmits<{ picked: [] }>()

const paneId = inject<1 | 2>('paneId', 1)
const paneNavigation = usePaneNavigation()
const { openEdition } = useHebrewBooks()
const sideEditionStore = useHebrewBooksSideEditionStore()
const { isSideBySideAvailable, isToggleOn, opensSideBySide } = useHebrewBooksSideEditionMode(paneId)

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
    .map((part) => part.trim())
    .filter(Boolean)
    .join(' · ')
}

function onSelect(edition: HebrewBooksEdition) {
  emit('picked')
  if (opensSideBySide.value) sideEditionStore.pickEdition(paneId, paneNavigation.activeTab.id, paneNavigation.activeTab.bookId, edition.id)
  else openEdition(edition, 'newTab', paneNavigation.activeTab.tocPath)
}
</script>

<template>
  <label v-if="isSideBySideAvailable" class="side-panel-toggle-row" @click.stop>
    <input v-model="isToggleOn" type="checkbox" />
    <span>{{ $t('bookView.hebrewBooks.showInSidePanel') }}</span>
  </label>
  <button dir="rtl"
    v-for="edition in editions"
    :key="edition.id"
    role="menuitem"
    class="edition-row"
    :class="{ 'edition-row-shown': isShownSideBySide(edition) }"
    :aria-current="isShownSideBySide(edition) || undefined"
    :title="edition.title"
    @click="onSelect(edition)"
  >
    <span class="edition-title-line">
      <span class="edition-title">{{ edition.title }}</span>
      <span v-if="edition.isBest" class="edition-tag edition-tag-best">{{ $t('bookView.hebrewBooks.recommended') }}</span>
      <span v-if="!edition.isCertain" class="edition-tag edition-tag-uncertain">{{ $t('bookView.hebrewBooks.possibleMatch') }}</span>
    </span>
    <span v-if="detailsFor(edition)" class="edition-details">{{ detailsFor(edition) }}</span>
  </button>
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
.side-panel-toggle-row input {
  margin: 0;
  accent-color: var(--accent-color);
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
