<script setup lang="ts">
/**
 * "מהדורות בהיברו בוקס" dropdown in the BookView toolbar — opens a scanned edition of
 * this book from HebrewBooks.
 *
 * An edition is a separate PDF, not an overlay, so unlike BookViewVersionsDropdown beside
 * it this never touches the text: the pick opens a new tab through the same HebrewBooks
 * flow as the HebrewBooks page (local folder / cache / download), and the PDF opens at
 * the outline entry for the section the reader is in now — read at click time, so it is
 * wherever they scrolled to, not where they were when the book opened.
 *
 * The button shows in every book (see `hasHebrewBooksEditions` in the toolbar): with no
 * catalog suggestions it still lists the reader's own items and the add options. It hosts
 * the "add from HebrewBooks" search window, which outlives the closed panel. It is collapsible: in
 * the toolbar's "more" menu it is a row whose list opens as a submenu (moreMenuRow).
 */
import { ref, computed, inject } from 'vue'
import { storeToRefs } from 'pinia'
import { IconDocumentPdf20Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useBookViewStore } from '@/stores/bookViewStore'
import BookViewHebrewBooksEditionsList from './BookViewHebrewBooksEditionsList.vue'
import BookViewHebrewBooksAddDialog from './BookViewHebrewBooksAddDialog.vue'
import { userItemAsEdition } from './useBookEditionOpen'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useUserEditionsStore } from '@/stores/userEditionsStore'
import type { HebrewBook } from '@/features/hebrewbooks/hebrewBooksCatalog'
import { submenuRow } from '@/composables/useToolbarMoreMenu'
import type { ContextMenuLeafItem } from '@/components/ContextMenu.vue'
import { useBookEditionsChoices } from './useBookEditionsChoices'
import { useI18n } from 'vue-i18n'
import { IconDelete16Regular } from '@iconify-prerendered/vue-fluent'
import type { HebrewBooksEdition } from '@/webview-host/queries.types'

const props = defineProps<{
  editions: HebrewBooksEdition[]
  talmudPdfPath: string | null
}>()

const emit = defineEmits<{ 'open-change': [isOpen: boolean] }>()

const { t } = useI18n()
const bookViewStore = useBookViewStore()
const { toolbarPosition } = storeToRefs(bookViewStore)

// ── Open / close ──────────────────────────────────────────────────────────────

const isOpen = ref(false)
const dropdownRef = ref<HTMLElement | null>(null)
const toggleButtonRef = ref<HTMLElement | null>(null)

function setOpen(value: boolean) {
  isOpen.value = value
  emit('open-change', value)
}

const { justClosed } = useDropdownClose(dropdownRef, () => setOpen(false), {
  toggleButton: toggleButtonRef,
})

function toggleOpen() {
  if (justClosed.value) return
  setOpen(!isOpen.value)
}

// ── Add from HebrewBooks ─────────────────────────────────────────────────────

const paneNavigation = usePaneNavigation()
const userEditionsStore = useUserEditionsStore()
const isAddDialogOpen = ref(false)

function openAddDialog() {
  setOpen(false)
  isAddDialogOpen.value = true
}

/** Saves the pick as this book's user item, then opens it at the current section. */
async function onAddHebrewBook(book: HebrewBook) {
  isAddDialogOpen.value = false
  const row = await userEditionsStore.addHebrewBook(paneNavigation.activeTab.bookId ?? 0, String(book.id), book.title)
  if (row) choices.openHebrewBooksEdition(userItemAsEdition(row))
}

const choices = useBookEditionsChoices(() => props.editions, () => props.talmudPdfPath, () => setOpen(false))

/**
 * This control as a row of the toolbar's "more" menu: the same list as a submenu - the side-panel
 * options, the reader's own items (the bin at a row's end drops it), the tractate, the catalog
 * suggestions, and the two add options.
 */
function moreMenuRow() {
  const c = choices
  const items: ContextMenuLeafItem[] = []
  if (c.sideBySide.isSideBySideAvailable.value) {
    items.push({ type: 'checkbox', label: t('bookView.hebrewBooks.showInSidePanel'), checked: c.sideBySide.isToggleOn.value, onChange: (on) => { c.sideBySide.isToggleOn.value = on } })
    if (c.sideBySide.isToggleOn.value) {
      for (const side of ['right', 'left'] as const) {
        items.push({
          type: 'checkbox',
          label: `${t('bookView.hebrewBooks.sidePanelSide')}: ${t(side === 'right' ? 'bookView.hebrewBooks.sidePanelRight' : 'bookView.hebrewBooks.sidePanelLeft')}`,
          checked: c.sideBySide.side.value === side,
          onChange: () => { c.sideBySide.side.value = side },
        })
      }
    }
    items.push({ type: 'separator' })
  }
  if (c.userEditions.value.length) {
    items.push({ type: 'heading', label: t('bookView.hebrewBooks.userItems') })
    for (const item of c.userEditions.value) {
      const reason = c.unavailableReason(item)
      items.push({
        label: reason ? `${item.title} (${reason})` : item.title,
        title: reason || item.filePath || item.title,
        disabled: !!reason,
        action: () => c.openUserItem(item),
        endAction: { icon: IconDelete16Regular, title: t('bookView.hebrewBooks.remove'), action: () => c.removeUserItem(item) },
      })
    }
  }
  if (props.talmudPdfPath) items.push({ label: t('bookView.hebrewBooks.otzariaTalmud'), title: props.talmudPdfPath, action: () => c.openTalmud() })
  if (c.visibleEditions.value.length) {
    if (c.userEditions.value.length || props.talmudPdfPath) items.push({ type: 'heading', label: t('bookView.hebrewBooks.suggestions') })
    for (const edition of c.visibleEditions.value) {
      const tags = [
        edition.isBest ? t('bookView.hebrewBooks.recommended') : '',
        edition.isCertain ? '' : t('bookView.hebrewBooks.possibleMatch'),
      ].filter(Boolean)
      items.push({
        label: tags.length ? `${edition.title} (${tags.join(', ')})` : edition.title,
        title: [edition.title, c.detailsFor(edition)].filter(Boolean).join('\n'),
        action: () => c.openHebrewBooksEdition(edition),
      })
    }
  }
  items.push({ type: 'separator' })
  items.push({ label: t('bookView.hebrewBooks.addFile'), action: () => void c.addPdfFromComputer() })
  items.push({ label: t('bookView.hebrewBooks.addFromHebrewBooks'), action: openAddDialog })
  return submenuRow(t('bookView.hebrewBooks.title'), IconDocumentPdf20Regular, items, { onOpen: c.loadUserEditions })
}
defineExpose({ moreMenuRow })

const dropdownPositionClass = computed(() => `dropdown-${toolbarPosition.value}`)

</script>

<template>
  <div class="editions-wrapper">
    <button
      ref="toggleButtonRef"
      class="editions-toggle"
      :class="{ active: isOpen }"
      :title="$t('bookView.hebrewBooks.title')"
      :aria-label="$t('bookView.hebrewBooks.title')"
      @click="toggleOpen"
    >
      <IconDocumentPdf20Regular />
    </button>

    <div v-if="isOpen" ref="dropdownRef" class="editions-dropdown" :class="dropdownPositionClass">
      <BookViewHebrewBooksEditionsList :editions="editions" :talmud-pdf-path="talmudPdfPath" @picked="setOpen(false)" @add-hebrew-book="openAddDialog" />
    </div>
    <BookViewHebrewBooksAddDialog
      v-if="isAddDialogOpen"
      :initial-term="paneNavigation.activeTab.title"
      @pick="onAddHebrewBook"
      @close="isAddDialogOpen = false"
    />
  </div>
</template>

<style scoped>
.editions-wrapper {
  position: relative;
}

/* ── Toggle button — inherits toolbar button styles from main.css ── */
.editions-toggle {
  display: flex;
  align-items: center;
  justify-content: center;
  /* The toolbar's button size — the overflow arithmetic charges every control one of these. */
  width: var(--toolbar-button-size);
  height: var(--toolbar-button-size);
  padding: 6px;
  border-radius: 4px;
  flex-shrink: 0;
}
.editions-toggle.active {
  color: var(--accent-color);
}

/* ── Dropdown panel ── */
.editions-dropdown {
  position: absolute;
  min-width: 180px;
  max-width: 300px;
  max-height: 320px;
  overflow-y: auto;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25);
  z-index: 100;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

/* ── Positioning based on toolbar position ── */
.dropdown-top {
  top: calc(100% + 4px);
  inset-inline-start: 0;
}

.dropdown-bottom {
  bottom: calc(100% + 4px);
  inset-inline-start: 0;
}

.dropdown-left {
  top: 0;
  inset-inline-end: calc(100% + 4px);
}

.dropdown-right {
  top: 0;
  inset-inline-start: calc(100% + 4px);
}

</style>
