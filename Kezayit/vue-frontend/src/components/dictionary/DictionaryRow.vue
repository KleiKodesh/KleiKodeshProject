<script setup lang="ts">
import type { DictSenseDisplay } from './useKezayitDictionary'
import { useSettingsStore } from '@/stores/settingsStore'
import { censorDivineNames } from '@/utils/censorDivineNames'

const props = defineProps<{ sense: DictSenseDisplay }>()

const settings = useSettingsStore()

function maybeFilter(text: string): string {
  return settings.censorDivineNames ? censorDivineNames(text) : text
}
</script>

<template>
  <div class="dict-row">
    <span class="dict-headword">{{ sense.headword }}</span>
    <template v-if="sense.definition">
      <span class="dict-sep">—</span>
      <span class="dict-definition">{{ maybeFilter(sense.definition) }}</span>
    </template>
    <span v-else class="dict-spacer" />
    <span v-if="sense.sourceLabel" class="dict-source">{{ sense.sourceLabel }}</span>
  </div>
</template>

<style scoped>
.dict-row {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 38px;
  padding: 0 14px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
  direction: rtl;
  overflow: hidden;
}

.dict-headword {
  font-size: 13px;
  font-weight: 700;
  color: var(--text-primary);
  flex-shrink: 0;
  white-space: nowrap;
}

.dict-sep {
  font-size: 13px;
  color: var(--text-secondary);
  flex-shrink: 0;
}

.dict-definition {
  font-size: 13px;
  color: var(--text-primary);
  flex: 1;
  min-width: 0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.dict-spacer {
  flex: 1;
}

.dict-source {
  font-size: 10px;
  color: var(--text-secondary);
  background: color-mix(in srgb, var(--text-secondary) 10%, transparent);
  border-radius: 999px;
  padding: 0 6px;
  line-height: 16px;
  flex-shrink: 0;
  align-self: center;
}
</style>
