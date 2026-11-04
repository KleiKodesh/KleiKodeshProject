<script setup lang="ts">
import { IconTextStrikethrough20Regular, IconTextStrikethrough20Filled } from '@iconify-prerendered/vue-fluent'
import ToolbarSplitDropdown from '@/components/ToolbarSplitDropdown.vue'
import { SKIP_OPTION_KEYS, type ReadAloudSkipOptions } from './readAloudSkip'

/**
 * What read aloud skips: the main half turns skipping on/off, the chevron opens the
 * checkboxes choosing what is skipped. The shared split button, like clean text.
 */
const enabled = defineModel<boolean>('enabled', { required: true })
const options = defineModel<ReadAloudSkipOptions>('options', { required: true })
const emit = defineEmits<{ changed: [] }>()

function toggleEnabled() {
  enabled.value = !enabled.value
  emit('changed')
}
function toggleOption(key: keyof ReadAloudSkipOptions) {
  options.value = { ...options.value, [key]: !options.value[key] }
  emit('changed')
}
</script>

<template>
  <ToolbarSplitDropdown
    :active="enabled"
    :main-title="$t('bookView.readAloud.skip')"
    :options-title="$t('bookView.readAloud.skipOptions')"
    @toggle="toggleEnabled"
  >
    <template #icon>
      <IconTextStrikethrough20Filled v-if="enabled" />
      <IconTextStrikethrough20Regular v-else />
    </template>
    <template #label>{{ $t('bookView.readAloud.skip') }}</template>
    <label v-for="key in SKIP_OPTION_KEYS" :key="key" class="option-row">
      <input type="checkbox" :checked="options[key]" @change="toggleOption(key)" />
      <span>{{ $t(`bookView.readAloud.skipOption.${key}`) }}</span>
    </label>
  </ToolbarSplitDropdown>
</template>
