<script setup lang="ts">
import { computed, ref, type Component } from 'vue'
import { useI18n } from 'vue-i18n'
import { IconOptions20Regular, IconHighlight20Regular, IconTextCaseTitle20Regular, IconTextWholeWord20Regular, IconTextAsterisk20Regular } from '@iconify-prerendered/vue-fluent'
import ContextMenu, { type ContextMenuItem } from '@/components/ContextMenu.vue'
import { PDF_KEYS, withKey } from './usePdfReaderShortcuts'

/** PDF.js's find options, for the search bar's options slot. */
export interface PdfFindOptionsModel {
  highlightAll: boolean
  caseSensitive: boolean
  entireWord: boolean
  matchDiacritics: boolean
}
const options = defineModel<PdfFindOptionsModel>({ required: true })
const { t } = useI18n()

const button = ref<HTMLElement | null>(null)
const menu = ref<InstanceType<typeof ContextMenu> | null>(null)
const ICONS: Record<keyof PdfFindOptionsModel, Component> = {
  highlightAll: IconHighlight20Regular,
  caseSensitive: IconTextCaseTitle20Regular,
  entireWord: IconTextWholeWord20Regular,
  matchDiacritics: IconTextAsterisk20Regular,
}
const KEYS: (keyof PdfFindOptionsModel)[] = ['highlightAll', 'caseSensitive', 'entireWord', 'matchDiacritics']
const items = computed<ContextMenuItem[]>(() =>
  KEYS.map((k) => ({
    type: 'checkbox',
    label: withKey(t(`pdfReader.find.${k}`), PDF_KEYS.find[k]),
    icon: ICONS[k],
    checked: options.value[k],
    onChange: (v: boolean) => (options.value = { ...options.value, [k]: v }),
  })),
)
/** Lit when anything differs from the defaults (highlight all on, the rest off). */
const changed = computed(() => !options.value.highlightAll || options.value.caseSensitive || options.value.entireWord || options.value.matchDiacritics)
function open() {
  const r = button.value?.getBoundingClientRect()
  if (r) void menu.value?.showAtPosition(r.left, r.bottom)
}
</script>

<template>
  <button
    ref="button"
    class="find-options-btn"
    :class="{ active: changed }"
    :title="$t('pdfReader.find.options')"
    aria-haspopup="menu"
    @mousedown.prevent
    @click="open"
  >
    <IconOptions20Regular />
  </button>
  <ContextMenu ref="menu" :items="items" />
</template>

<style scoped>
/* The search bar's own nav-button size. */
.find-options-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  flex-shrink: 0;
  border-radius: 4px;
  cursor: pointer;
}
.find-options-btn svg {
  width: 16px;
  height: 16px;
}
.find-options-btn.active {
  color: var(--accent-color);
}
</style>
