<script setup lang="ts">
/**
 * The rows of a book's scanned editions in the toolbar dropdown (BookViewHebrewBooksEditionsDropdown).
 * What the rows offer and what picking one does lives in useBookEditionsChoices, shared with the
 * dropdown's row in the toolbar's "more" menu. Top to bottom:
 *
 *  - the reader's own items: PDFs from their computer and HebrewBooks books they added, each with
 *    a trash button on hover that drops it from the list (the file itself is never touched);
 *  - Otzaria's scanned tractate (Talmud Bavli), when it is on this computer;
 *  - the catalog suggestions: one already on the computer always, the rest only while online;
 *  - "add a PDF from the computer" and "add from HebrewBooks".
 *
 * "Add from HebrewBooks" is asked of the host (`add-hebrew-book`): its search window must outlive
 * this list, which closes with the dropdown. The host only closes itself, on `picked`.
 */
import { IconDelete16Regular } from '@iconify-prerendered/vue-fluent'
import { useBookEditionsChoices } from './useBookEditionsChoices'
import type { HebrewBooksEdition } from '@/webview-host/queries.types'

const props = defineProps<{
  editions: HebrewBooksEdition[]
  talmudPdfPath: string | null
}>()

const emit = defineEmits<{ picked: []; 'add-hebrew-book': [] }>()

const {
  sideBySide: { isSideBySideAvailable, isToggleOn, side },
  userEditions,
  visibleEditions,
  isShownSideBySide,
  detailsFor,
  unavailableReason,
  openHebrewBooksEdition,
  openUserItem: onUserItem,
  openTalmud: onTalmud,
  removeUserItem: onRemove,
  addPdfFromComputer: onAddFile,
} = useBookEditionsChoices(() => props.editions, () => props.talmudPdfPath, () => emit('picked'))
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

/* Always in view: pinned to the bottom of the scrolling panel. */
.add-actions {
  position: sticky;
  bottom: 0;
  z-index: 1;
  border-top: 1px solid var(--border-color);
  background: var(--bg-secondary);
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
