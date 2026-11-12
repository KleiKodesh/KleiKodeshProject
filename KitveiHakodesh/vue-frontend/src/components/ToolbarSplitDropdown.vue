<script setup lang="ts">
/**
 * A reading-toolbar split button: the main half toggles a feature, the chevron half (behind a
 * separator) opens a dropdown of its options. Used by the book view's clean-text combo and
 * FastDocView's notes-place button, so both look and behave the same. The dropdown's rows go in
 * the default slot as `.option-row` elements (28 px rows, 180-300 px wide).
 */
import { ref } from 'vue'
import { storeToRefs } from 'pinia'
import { IconChevronDown12Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useDropdownViewportFit } from '@/composables/useDropdownViewportFit'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useToolbarOverflowSubmenu } from '@/composables/useToolbarOverflowSubmenu'

const props = defineProps<{
  active: boolean
  mainTitle: string
  optionsTitle: string
  disabled?: boolean
  /** Renders as a row of the toolbar's "more" flyout: its body toggles, the chevron
   *  at its end opens the options as a side submenu. */
  inOverflow?: boolean
}>()
const emit = defineEmits<{ toggle: [] }>()

const { toolbarPosition } = storeToRefs(useBookViewStore())

const isOpen = ref(false)
const dropdownRef = ref<HTMLElement | null>(null)
const toggleButtonRef = ref<HTMLElement | null>(null)

const { justClosed } = useDropdownClose(dropdownRef, () => (isOpen.value = false), {
  toggleButton: toggleButtonRef,
})
useDropdownViewportFit(dropdownRef)
const submenuStyle = useToolbarOverflowSubmenu(() => !!props.inOverflow, isOpen, toggleButtonRef, dropdownRef)

function toggleOpen() {
  if (justClosed.value) return
  isOpen.value = !isOpen.value
}
function close() {
  isOpen.value = false
}
defineExpose({ close })
</script>

<template>
  <div v-if="inOverflow" role="group" class="overflow-host split-dropdown-overflow-row">
    <button
      role="menuitemcheckbox"
      class="overflow-row"
      :class="{ active }"
      :aria-checked="active"
      :disabled="disabled"
      :title="mainTitle"
      @click="emit('toggle')"
    >
      <slot name="icon" />
      <span><slot name="label" /></span>
    </button>
    <button
      ref="toggleButtonRef"
      role="menuitem"
      aria-haspopup="menu"
      class="split-dropdown-overflow-chevron"
      :class="{ open: isOpen }"
      :disabled="disabled"
      :title="optionsTitle"
      @click="toggleOpen"
    >
      <IconChevronDown12Regular />
    </button>

    <div v-if="isOpen" ref="dropdownRef" class="split-dropdown-panel overflow-submenu" title="" :style="submenuStyle">
      <slot />
    </div>
  </div>
  <div
    v-else
    class="split-dropdown"
    :class="{ active, vertical: toolbarPosition === 'left' || toolbarPosition === 'right' }"
  >
    <button class="split-dropdown-main" :disabled="disabled" :title="mainTitle" @click="emit('toggle')">
      <slot name="icon" />
    </button>
    <button
      ref="toggleButtonRef"
      class="split-dropdown-chevron"
      :class="{ open: isOpen }"
      :disabled="disabled"
      :title="optionsTitle"
      @click="toggleOpen"
    >
      <IconChevronDown12Regular />
    </button>

    <div v-if="isOpen" ref="dropdownRef" class="split-dropdown-panel" title="" :class="`dropdown-${toolbarPosition}`">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.split-dropdown {
  position: relative;
  display: flex;
  align-items: center;
  flex-shrink: 0;
}

/* ── Two halves - the main half mirrors the toolbar's button sizing ── */
button {
  display: flex;
  align-items: center;
  justify-content: center;
  height: var(--toolbar-button-size);
  border-radius: 4px;
  flex-shrink: 0;
}
.split-dropdown-main {
  width: var(--toolbar-button-size);
  padding: 6px;
}
/* Separators on both sides, so the chevron reads as the main button's own options, not a stray button. */
.split-dropdown {
  border-inline: 1px solid var(--border-color);
  padding-inline: 2px;
}
.split-dropdown-chevron {
  /* Sizes here feed CLEAN_TEXT_COMBO_WIDTH_IN_BUTTONS - change both together. */
  width: 14px;
  padding: 0;
}
/* On a vertical toolbar the halves stack, so the combo stays one button wide. */
.vertical {
  flex-direction: column;
  border-inline: none;
  border-block: 1px solid var(--border-color);
  padding-inline: 0;
  padding-block: 2px;
}
.vertical .split-dropdown-chevron {
  width: var(--toolbar-button-size);
  height: 14px;
}
/* In the flyout: the row's body and the chevron side by side. */
.split-dropdown-overflow-row {
  display: flex;
  align-items: center;
}
.split-dropdown-overflow-chevron {
  width: var(--toolbar-button-size);
}
.split-dropdown-overflow-chevron.open,
.split-dropdown-chevron.open {
  color: var(--accent-color);
}
.active .split-dropdown-main {
  color: var(--accent-color);
}
button:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

/* ── Dropdown panel (BookViewWordLinkMarkersDropdown look) ── */
.split-dropdown-panel {
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
  padding: 2px 0;
}

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

.split-dropdown-panel :slotted(.option-row) {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 28px;
  padding: 0 10px;
  font-size: 12px;
  color: var(--text-primary);
  white-space: nowrap;
  cursor: pointer;
}
.split-dropdown-panel :slotted(.option-row:hover) {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.split-dropdown-panel :slotted(.option-row input) {
  accent-color: var(--accent-color);
  cursor: pointer;
}
.split-dropdown-panel :slotted(.option-row:disabled) {
  opacity: 0.5;
  cursor: default;
}
</style>
