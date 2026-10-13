<script setup lang="ts">
/**
 * "פרטי הספר" dropdown in the BookView toolbar - shows what the library records ABOUT the
 * open book: authors, category, publication place and date, licence, source and so on.
 *
 * The content itself is BookViewBookInfoPanel, mounted only while open. This button is the
 * first control to collapse into the toolbar's overflow flyout, which shows the same panel.
 */
import { ref, computed, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { IconInfo20Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useBookViewStore } from '@/stores/bookViewStore'
import BookViewBookInfoPanel from './BookViewBookInfoPanel.vue'

const props = defineProps<{
  bookId: number | undefined
  /** True once the toolbar has collapsed this button into its overflow flyout. */
  isCollapsed?: boolean
}>()

const bookViewStore = useBookViewStore()
const { toolbarPosition } = storeToRefs(bookViewStore)

const BUTTON_LABEL = 'פרטי הספר'

// ── Open / close ──────────────────────────────────────────────────────────────

const isOpen = ref(false)
const dropdownRef = ref<HTMLElement | null>(null)
const toggleButtonRef = ref<HTMLElement | null>(null)

const { justClosed } = useDropdownClose(dropdownRef, () => (isOpen.value = false), {
  toggleButton: toggleButtonRef,
})

function toggleOpen() {
  if (justClosed.value) return
  isOpen.value = !isOpen.value
}

// A popup left open over a button that has collapsed away would hang over nothing.
watch(
  () => props.isCollapsed,
  (collapsed) => {
    if (collapsed) isOpen.value = false
  },
)

const dropdownPositionClass = computed(() => `dropdown-${toolbarPosition.value}`)

</script>

<template>
  <div class="book-info-wrapper">
    <button
      ref="toggleButtonRef"
      :class="{ active: isOpen }"
      :disabled="bookId == null"
      :title="BUTTON_LABEL"
      :aria-expanded="isOpen"
      @click="toggleOpen"
    >
      <IconInfo20Regular />
    </button>

    <div v-if="isOpen" ref="dropdownRef" class="book-info-dropdown" :class="dropdownPositionClass">
      <BookViewBookInfoPanel :book-id="bookId" />
    </div>
  </div>
</template>

<style scoped>
.book-info-wrapper {
  position: relative;
  /* A fixed box, so the toolbar can collapse it into the overflow flyout like a button. */
  width: var(--toolbar-button-size);
  height: var(--toolbar-button-size);
  flex-shrink: 0;
}

/* ── Toggle button — the toolbar's button size, so the overflow arithmetic stays true ── */
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
button svg {
  width: 16px;
  height: 16px;
}
button.active {
  color: var(--accent-color);
}
button:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

/* ── Dropdown panel ── */
.book-info-dropdown {
  position: absolute;
  width: max-content;
  min-width: 220px;
  max-width: 360px;
  max-height: 420px;
  overflow-y: auto;
  padding: 8px 12px;
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

</style>
