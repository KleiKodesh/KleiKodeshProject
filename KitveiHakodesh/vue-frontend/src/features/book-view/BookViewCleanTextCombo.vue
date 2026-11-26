<script setup lang="ts">
/**
 * The clean-text combo button in the BookView toolbar: the eraser half turns cleaning
 * on/off (Ctrl+D), the chevron half opens five checkboxes: nikkud, teamim, remove all
 * punctuation, remove only modern punctuation (with the dibbur hamatchil dash), quotes.
 * The two punctuation options exclude each other; "all" already removes quotes.
 * Both the on/off state and the options are global settings - one choice for every book.
 */
import { computed } from 'vue'
import { storeToRefs } from 'pinia'
import { IconEraser20Regular, IconEraser20Filled } from '@iconify-prerendered/vue-fluent'
import { submenuRow } from '@/composables/useToolbarMoreMenu'
import type { ContextMenuLeafItem } from '@/components/ContextMenu.vue'
import { useI18n } from 'vue-i18n'
import ToolbarSplitDropdown from '@/components/ToolbarSplitDropdown.vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { CLEAN_TEXT } from '@/utils/hebrewTextProcessing'
import { ALL_PUNCTUATION, MODERN_PUNCTUATION, PUNCTUATION_MASK, QUOTES_MASK, QUOTES_REMOVED } from '@/utils/punctuationModes'

const { t } = useI18n()
const settingsStore = useSettingsStore()
const { cleanTextFlags, cleanTextOptions } = storeToRefs(settingsStore)

/** Punctuation bits other than quotes - what picks "all" vs "modern only". */
const punctuation = computed(() => cleanTextOptions.value & PUNCTUATION_MASK & ~(QUOTES_MASK))
const removeAll = computed(() => punctuation.value === (ALL_PUNCTUATION & ~(QUOTES_MASK)))
const removeModern = computed(() => punctuation.value === MODERN_PUNCTUATION)
const removeQuotes = computed(() => (cleanTextOptions.value & QUOTES_MASK) === QUOTES_REMOVED)

function toggleFlag(flag: number) {
  cleanTextOptions.value ^= flag
}

/** Sets the punctuation preset (or none), keeping nikkud/teamim and the quotes toggle. */
function setPunctuation(preset: number, dibburHamatchil: boolean) {
  const kept = cleanTextOptions.value & ~PUNCTUATION_MASK & ~CLEAN_TEXT.DIBBUR_HAMATCHIL_DASH
  // Quotes removed only because "all" was on go with it.
  const quotes = removeQuotes.value && !removeAll.value ? QUOTES_REMOVED : 0
  cleanTextOptions.value = kept | preset | quotes | (dibburHamatchil ? CLEAN_TEXT.DIBBUR_HAMATCHIL_DASH : 0)
}

function toggleAll() {
  setPunctuation(removeAll.value ? 0 : ALL_PUNCTUATION, false)
}

function toggleModern() {
  setPunctuation(removeModern.value ? 0 : MODERN_PUNCTUATION, !removeModern.value)
}

function toggleQuotes() {
  cleanTextOptions.value ^= QUOTES_REMOVED
}

/**
 * This control as a row of the toolbar's "more" menu: the on/off switch (Ctrl+D) first, then the
 * five options. They are independent toggles, so the menu stays open while you flip several.
 */
function moreMenuRow() {
  const items: ContextMenuLeafItem[] = [
    { type: 'checkbox', label: t('bookView.toolbar.cleanText'), title: `${t('bookView.toolbar.cleanText')} (Ctrl+D)`, icon: IconEraser20Regular, checked: cleanTextFlags.value !== 0, onChange: () => settingsStore.toggleCleanText() },
    { type: 'separator' },
    { type: 'checkbox', label: t('bookView.toolbar.cleanNikkud'), checked: (cleanTextOptions.value & CLEAN_TEXT.NIKKUD) !== 0, onChange: () => toggleFlag(CLEAN_TEXT.NIKKUD) },
    { type: 'checkbox', label: t('bookView.toolbar.cleanTeamim'), checked: (cleanTextOptions.value & CLEAN_TEXT.TEAMIM) !== 0, onChange: () => toggleFlag(CLEAN_TEXT.TEAMIM) },
    { type: 'checkbox', label: t('bookView.toolbar.cleanAllPunctuation'), checked: removeAll.value, onChange: toggleAll },
    { type: 'checkbox', label: t('bookView.toolbar.cleanModernPunctuation'), checked: removeModern.value, onChange: toggleModern },
    { type: 'checkbox', label: t('bookView.toolbar.cleanQuotes'), checked: removeAll.value || removeQuotes.value, disabled: removeAll.value, onChange: toggleQuotes },
  ]
  return submenuRow(t('bookView.toolbar.cleanText'), cleanTextFlags.value !== 0 ? IconEraser20Filled : IconEraser20Regular, items)
}
defineExpose({ moreMenuRow })
</script>

<template>
  <ToolbarSplitDropdown
    class="clean-text-combo"
    :active="cleanTextFlags !== 0"
    :main-title="`${$t('bookView.toolbar.cleanText')} (Ctrl+D)`"
    :options-title="$t('bookView.toolbar.cleanTextOptions')"
    @toggle="settingsStore.toggleCleanText()"
  >
    <template #icon>
      <IconEraser20Filled v-if="cleanTextFlags !== 0" />
      <IconEraser20Regular v-else />
    </template>
    <template #label>{{ $t('bookView.toolbar.cleanText') }}</template>
    <label class="option-row">
      <input type="checkbox" :checked="(cleanTextOptions & CLEAN_TEXT.NIKKUD) !== 0" @change="toggleFlag(CLEAN_TEXT.NIKKUD)" />
      <span>{{ $t('bookView.toolbar.cleanNikkud') }}</span>
    </label>
    <label class="option-row">
      <input type="checkbox" :checked="(cleanTextOptions & CLEAN_TEXT.TEAMIM) !== 0" @change="toggleFlag(CLEAN_TEXT.TEAMIM)" />
      <span>{{ $t('bookView.toolbar.cleanTeamim') }}</span>
    </label>
    <label class="option-row">
      <input type="checkbox" :checked="removeAll" @change="toggleAll" />
      <span>{{ $t('bookView.toolbar.cleanAllPunctuation') }}</span>
    </label>
    <label class="option-row">
      <input type="checkbox" :checked="removeModern" @change="toggleModern" />
      <span>{{ $t('bookView.toolbar.cleanModernPunctuation') }}</span>
    </label>
    <label class="option-row">
      <input type="checkbox" :checked="removeAll || removeQuotes" :disabled="removeAll" @change="toggleQuotes" />
      <span>{{ $t('bookView.toolbar.cleanQuotes') }}</span>
    </label>
  </ToolbarSplitDropdown>
</template>
