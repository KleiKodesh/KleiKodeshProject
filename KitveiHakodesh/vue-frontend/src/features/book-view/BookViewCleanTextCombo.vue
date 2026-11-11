<script setup lang="ts">
/**
 * The clean-text combo button in the BookView toolbar: the eraser half turns cleaning
 * on/off (Ctrl+D), the chevron half opens what cleaning does. Two kinds of choice:
 *   - reading preferences: vowels, cantillation, maqaf, the dibbur hamatchil dash;
 *   - punctuation, from modern to the classic rabbinic style: a level picks every mark at
 *     once, and each mark can then be set on its own (keep / convert / remove).
 * Both the on/off state and the options are global settings - one choice for every book.
 * The split button itself is the shared ToolbarSplitDropdown.
 */
import { computed } from 'vue'
import { storeToRefs } from 'pinia'
import { IconEraser20Regular, IconEraser20Filled } from '@iconify-prerendered/vue-fluent'
import ToolbarSplitDropdown from '@/components/ToolbarSplitDropdown.vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { CLEAN_TEXT } from '@/utils/hebrewTextProcessing'
import {
  MARK_MODE, PUNCTUATION_MARKS, PUNCTUATION_MASK, PUNCTUATION_PRESETS, markMode, withMarkMode,
  type MarkMode, type PunctuationMark,
} from '@/utils/punctuationModes'

defineProps<{
  /** Renders as a row of the toolbar's "more" flyout. */
  inOverflow?: boolean
}>()

const settingsStore = useSettingsStore()
const { cleanTextFlags, cleanTextOptions } = storeToRefs(settingsStore)

/** One checkbox per reading-preference toggle, in the order the dropdown lists them. */
const TOGGLES = [
  { flag: CLEAN_TEXT.NIKKUD, labelKey: 'bookView.toolbar.cleanNikkud' },
  { flag: CLEAN_TEXT.TEAMIM, labelKey: 'bookView.toolbar.cleanTeamim' },
  { flag: CLEAN_TEXT.MAQAF, labelKey: 'bookView.toolbar.cleanMaqaf' },
  { flag: CLEAN_TEXT.DIBBUR_HAMATCHIL_DASH, labelKey: 'bookView.toolbar.cleanDibburHamatchilDash' },
] as const

function toggleOption(flag: number) {
  cleanTextOptions.value ^= flag
}

/** The level whose marks match the current ones exactly; none means "custom". */
const activePreset = computed(() => PUNCTUATION_PRESETS.find((p) => p.value === (cleanTextOptions.value & PUNCTUATION_MASK))?.key)

function applyPreset(value: number) {
  cleanTextOptions.value = (cleanTextOptions.value & ~PUNCTUATION_MASK) | value
}

function setMark(mark: PunctuationMark, mode: MarkMode) {
  cleanTextOptions.value = withMarkMode(cleanTextOptions.value, mark, mode)
}

function modeLabelKey(mark: PunctuationMark, mode: MarkMode): string {
  if (mode === MARK_MODE.KEEP) return 'bookView.toolbar.markMode.keep'
  if (mode === MARK_MODE.REMOVE) return 'bookView.toolbar.markMode.remove'
  return `bookView.toolbar.markMode.${mark.convertTo}`
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
    <label v-for="toggle in TOGGLES" :key="toggle.flag" class="option-row">
      <input
        type="checkbox"
        :checked="(cleanTextOptions & toggle.flag) !== 0"
        @change="toggleOption(toggle.flag)"
      />
      <span>{{ $t(toggle.labelKey) }}</span>
    </label>

    <div class="section-title">{{ $t('bookView.toolbar.punctuation') }}</div>
    <label v-for="level in PUNCTUATION_PRESETS" :key="level.key" class="option-row">
      <input
        type="radio"
        name="clean-text-punctuation-level"
        :checked="activePreset === level.key"
        @change="applyPreset(level.value)"
      />
      <span>{{ $t(`bookView.toolbar.punctuationLevel.${level.key}`) }}</span>
    </label>
    <label v-if="!activePreset" class="option-row">
      <input type="radio" name="clean-text-punctuation-level" checked disabled />
      <span>{{ $t('bookView.toolbar.punctuationLevel.custom') }}</span>
    </label>

    <div class="section-title">{{ $t('bookView.toolbar.punctuationMarks') }}</div>
    <div v-for="mark in PUNCTUATION_MARKS" :key="mark.key" class="option-row mark-row">
      <span class="mark-label">{{ $t(`bookView.toolbar.mark.${mark.key}`) }}</span>
      <span class="mark-modes" role="radiogroup" :aria-label="$t(`bookView.toolbar.mark.${mark.key}`)">
        <button
          v-for="mode in mark.modes"
          :key="mode"
          type="button"
          role="radio"
          class="mark-mode"
          :class="{ selected: markMode(cleanTextOptions, mark) === mode }"
          :aria-checked="markMode(cleanTextOptions, mark) === mode"
          @click="setMark(mark, mode)"
        >
          {{ $t(modeLabelKey(mark, mode)) }}
        </button>
      </span>
    </div>
  </ToolbarSplitDropdown>
</template>

<style scoped>
.section-title {
  padding: 6px 10px 2px;
  font-size: 11px;
  font-weight: 600;
  color: var(--text-secondary);
  border-top: 1px solid var(--border-color);
  margin-top: 4px;
}
.mark-row {
  justify-content: space-between;
  cursor: default;
}
.mark-label {
  overflow: hidden;
  text-overflow: ellipsis;
}
.mark-modes {
  display: inline-flex;
  flex-shrink: 0;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  overflow: hidden;
}
.mark-mode {
  padding: 1px 6px;
  font-size: 11px;
  background: transparent;
  color: var(--text-primary);
  border: none;
  cursor: pointer;
}
.mark-mode + .mark-mode {
  border-inline-start: 1px solid var(--border-color);
}
.mark-mode.selected {
  background: var(--accent-color);
  color: #fff;
}
</style>
