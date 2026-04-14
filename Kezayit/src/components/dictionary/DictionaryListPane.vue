<script setup lang="ts">
import { ref } from 'vue'
import { IconSearch20Regular } from '@iconify-prerendered/vue-fluent'
import { useListKeys } from '@/composables/useListKeyNav'
import type { DictSuggestion } from './useKezayitDictionary'

const props = defineProps<{ suggestions: DictSuggestion[] }>()
const emit = defineEmits<{ pick: [word: string] }>()

const listEl = ref<HTMLElement | null>(null)

const { focusedIndex } = useListKeys(
  listEl,
  () => props.suggestions.length,
  (i) => {
    const s = props.suggestions[i]
    if (s) emit('pick', s.headword)
  },
  { itemSelector: '.dict-suggestion-item' },
)

// Data is pre-cleaned in useDictSuggestions

defineExpose({ focus: () => listEl.value?.focus() })
</script>

<template>
  <div class="list-pane">
    <div v-if="suggestions.length > 0" ref="listEl" class="dict-suggestions" tabindex="0">
      <div
        v-for="(s, i) in suggestions"
        :key="`${s.headword}-${i}`"
        class="dict-suggestion-item"
        :class="{ focused: focusedIndex === i }"
        @click="emit('pick', s.headword)"
      >
        <span class="sugg-headword">{{ s.headword }}</span>
        <template v-if="s.definition">
          <span class="sugg-sep">-</span>
          <span class="sugg-def">{{ s.definition }}</span>
        </template>
      </div>
    </div>
    <div v-else class="dict-empty">
      <IconSearch20Regular class="dict-empty-icon" />
    </div>
  </div>
</template>

<style scoped>
.list-pane {
  flex: 1;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

.dict-empty {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: var(--text-secondary);
  opacity: 0.5;
}
.dict-empty-icon {
  width: 48px;
  height: 48px;
}

.dict-suggestions {
  display: flex;
  flex-direction: column;
}
.dict-suggestion-item {
  min-height: 44px;
  padding: 8px 14px;
  display: block;
  font-size: 13px;
  color: var(--text-primary);
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
  cursor: pointer;
  direction: rtl;
  line-height: 1.5;
}
.dict-suggestion-item:hover,
.dict-suggestion-item.focused {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.sugg-headword {
  font-weight: 600;
}
.sugg-sep {
  color: var(--text-secondary);
  margin-inline: 4px;
}
.sugg-def {
  color: var(--text-secondary);
  font-size: 12px;
}
</style>
