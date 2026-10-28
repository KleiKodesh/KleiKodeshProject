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
 * The button renders only when the book has editions, which the toolbar decides — see
 * `hasHebrewBooksEditions` there. It is collapsible: in the toolbar's "more" flyout it is a
 * row whose list opens as a side submenu (useOverflowSubmenu).
 */
import { ref, computed } from 'vue'
import { storeToRefs } from 'pinia'
import { IconDocumentPdf20Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useBookViewStore } from '@/stores/bookViewStore'
import BookViewHebrewBooksEditionsList from './BookViewHebrewBooksEditionsList.vue'
import { useOverflowSubmenu } from './useOverflowSubmenu'
import type { HebrewBooksEdition } from '@/webview-host/queries.types'

const props = defineProps<{
  editions: HebrewBooksEdition[]
  /** Renders as a row of the toolbar's "more" flyout, its panel a side submenu. */
  inOverflow?: boolean
}>()

const emit = defineEmits<{ 'open-change': [isOpen: boolean] }>()

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

const dropdownPositionClass = computed(() => `dropdown-${toolbarPosition.value}`)
const submenuStyle = useOverflowSubmenu(() => !!props.inOverflow, isOpen, toggleButtonRef, dropdownRef)

</script>

<template>
  <div class="editions-wrapper" :class="{ 'overflow-host': inOverflow }">
    <button
      ref="toggleButtonRef"
      class="editions-toggle"
      :class="{ active: isOpen, 'overflow-row': inOverflow }"
      :role="inOverflow ? 'menuitem' : undefined"
      :title="$t('bookView.hebrewBooks.title')"
      :aria-label="$t('bookView.hebrewBooks.title')"
      @click="toggleOpen"
    >
      <IconDocumentPdf20Regular />
      <span v-if="inOverflow">{{ $t('bookView.hebrewBooks.title') }}</span>
    </button>

    <div v-if="isOpen" ref="dropdownRef" class="editions-dropdown" :class="inOverflow ? 'overflow-submenu' : dropdownPositionClass" :style="submenuStyle">
      <div class="editions-header">{{ $t('bookView.hebrewBooks.title') }}</div>
      <BookViewHebrewBooksEditionsList :editions="editions" @picked="setOpen(false)" />
    </div>
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

.editions-header {
  padding: 6px 12px;
  font-size: 11px;
  color: var(--text-secondary);
  border-bottom: 1px solid var(--border-color);
}
</style>
