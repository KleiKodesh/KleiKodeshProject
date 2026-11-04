<script setup lang="ts">
/**
 * The clean-text combo button in the BookView toolbar: the eraser half turns cleaning
 * on/off (Ctrl+D), the chevron half opens checkboxes choosing what cleaning removes.
 * Both the on/off state and the options are global settings - one choice for every book.
 * The split button itself is the shared ToolbarSplitDropdown.
 */
import { storeToRefs } from 'pinia'
import { IconEraser20Regular, IconEraser20Filled } from '@iconify-prerendered/vue-fluent'
import ToolbarSplitDropdown from '@/components/ToolbarSplitDropdown.vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { CLEAN_TEXT } from '@/utils/hebrewTextProcessing'

defineProps<{
  /** Renders as a row of the toolbar's "more" flyout. */
  inOverflow?: boolean
}>()

const settingsStore = useSettingsStore()
const { cleanTextFlags, cleanTextOptions } = storeToRefs(settingsStore)

/** One checkbox per CLEAN_TEXT option, in the order the dropdown lists them. */
const OPTIONS = [
  { flag: CLEAN_TEXT.NIKKUD, labelKey: 'bookView.toolbar.cleanNikkud' },
  { flag: CLEAN_TEXT.TEAMIM, labelKey: 'bookView.toolbar.cleanTeamim' },
  { flag: CLEAN_TEXT.MAQAF, labelKey: 'bookView.toolbar.cleanMaqaf' },
  { flag: CLEAN_TEXT.DIBBUR_HAMATCHIL_DASH, labelKey: 'bookView.toolbar.cleanDibburHamatchilDash' },
  { flag: CLEAN_TEXT.NORMALIZE_PUNCTUATION, labelKey: 'bookView.toolbar.cleanNormalizePunctuation' },
  { flag: CLEAN_TEXT.REMOVE_PUNCTUATION, labelKey: 'bookView.toolbar.cleanRemovePunctuation' },
] as const

function toggleOption(flag: number) {
  cleanTextOptions.value ^= flag
}
</script>

<template>
  <ToolbarSplitDropdown
    class="clean-text-combo"
    :active="cleanTextFlags !== 0"
    :in-overflow="inOverflow"
    :main-title="`${$t('bookView.toolbar.cleanText')} (Ctrl+D)`"
    :options-title="$t('bookView.toolbar.cleanTextOptions')"
    @toggle="settingsStore.toggleCleanText()"
  >
    <template #icon>
      <IconEraser20Filled v-if="cleanTextFlags !== 0" />
      <IconEraser20Regular v-else />
    </template>
    <template #label>{{ $t('bookView.toolbar.cleanText') }}</template>
    <label v-for="option in OPTIONS" :key="option.flag" class="option-row">
      <input
        type="checkbox"
        :checked="(cleanTextOptions & option.flag) !== 0"
        @change="toggleOption(option.flag)"
      />
      <span>{{ $t(option.labelKey) }}</span>
    </label>
  </ToolbarSplitDropdown>
</template>
