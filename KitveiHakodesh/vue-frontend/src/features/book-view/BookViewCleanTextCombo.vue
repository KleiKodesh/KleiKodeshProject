<script setup lang="ts">
/**
 * The clean-text combo button in the BookView toolbar: the eraser half turns cleaning
 * on/off (Ctrl+D), the chevron half opens checkboxes choosing what cleaning removes.
 * Both the on/off state and the options are global settings - one choice for every book.
 */
import { ref } from 'vue'
import { storeToRefs } from 'pinia'
import { IconEraser20Regular, IconEraser20Filled, IconChevronDown12Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { useDropdownViewportFit } from '@/composables/useDropdownViewportFit'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'

const settingsStore = useSettingsStore()
const { cleanTextActive, cleanTextRemoveTeamim, cleanTextRemoveNikkud } = storeToRefs(settingsStore)
const { toolbarPosition } = storeToRefs(useBookViewStore())

const isOpen = ref(false)
const dropdownRef = ref<HTMLElement | null>(null)
const toggleButtonRef = ref<HTMLElement | null>(null)

const { justClosed } = useDropdownClose(dropdownRef, () => (isOpen.value = false), {
  toggleButton: toggleButtonRef,
})
useDropdownViewportFit(dropdownRef)

function toggleOpen() {
  if (justClosed.value) return
  isOpen.value = !isOpen.value
}
</script>

<template>
  <div
    class="clean-text-combo"
    :class="{ active: cleanTextActive, vertical: toolbarPosition === 'left' || toolbarPosition === 'right' }"
  >
    <button
      class="clean-text-main"
      :title="`${$t('bookView.toolbar.cleanText')} (Ctrl+D)`"
      @click="settingsStore.toggleCleanText()"
    >
      <IconEraser20Filled v-if="cleanTextActive" />
      <IconEraser20Regular v-else />
    </button>
    <button
      ref="toggleButtonRef"
      class="clean-text-chevron"
      :class="{ open: isOpen }"
      :title="$t('bookView.toolbar.cleanTextOptions')"
      @click="toggleOpen"
    >
      <IconChevronDown12Regular />
    </button>

    <div v-if="isOpen" ref="dropdownRef" class="clean-text-dropdown" :class="`dropdown-${toolbarPosition}`">
      <label class="option-row">
        <input v-model="cleanTextRemoveNikkud" type="checkbox" />
        <span>{{ $t('bookView.toolbar.cleanNikkud') }}</span>
      </label>
      <label class="option-row">
        <input v-model="cleanTextRemoveTeamim" type="checkbox" />
        <span>{{ $t('bookView.toolbar.cleanTeamim') }}</span>
      </label>
    </div>
  </div>
</template>

<style scoped>
.clean-text-combo {
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
.clean-text-main {
  width: var(--toolbar-button-size);
  padding: 6px;
}
/* Separators on both sides, so the chevron reads as the eraser's own options, not a stray button. */
.clean-text-combo {
  border-inline: 1px solid var(--border-color);
  padding-inline: 2px;
}
.clean-text-chevron {
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
.vertical .clean-text-chevron {
  width: var(--toolbar-button-size);
  height: 14px;
}
.clean-text-chevron.open {
  color: var(--accent-color);
}
.active .clean-text-main {
  color: var(--accent-color);
}

/* ── Dropdown panel (BookViewWordLinkMarkersDropdown look) ── */
.clean-text-dropdown {
  position: absolute;
  min-width: 180px;
  max-width: 300px;
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

.option-row {
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
.option-row:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.option-row input {
  accent-color: var(--accent-color);
  cursor: pointer;
}
</style>
