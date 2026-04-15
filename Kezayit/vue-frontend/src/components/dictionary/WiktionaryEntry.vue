<script setup lang="ts">
import { computed } from 'vue'
import type { WiktionarySense } from './useWiktionary'
import { useSettingsStore } from '@/stores/settingsStore'
import { censorDivineNames } from '@/utils/censorDivineNames'

const props = defineProps<{ sense: WiktionarySense; index: number; total: number }>()
defineEmits<{ searchWord: [word: string] }>()

const settings = useSettingsStore()

function maybeFilter(text: string): string {
  return settings.censorDivineNames ? censorDivineNames(text) : text
}

const SECTION_ORDER = [
  'גיזרון',
  'נגזרות',
  'מילים נרדפות',
  'ניגודים',
  'צירופים',
  'מידע נוסף',
  'ראו גם',
]

function stripNikud(s: string): string {
  return s.replace(/[\u05B0-\u05C7\u05F0-\u05F4\uFB1D-\uFB4E]/g, '').trim()
}
</script>

<template>
  <div class="entry">
    <!-- Sense header -->
    <div class="entry-head">
      <span v-if="total > 1" class="entry-idx">{{ index + 1 }}.</span>
      <span class="entry-nikud">{{ sense.nikud ?? sense.headword }}</span>
      <span v-if="sense.nikud && stripNikud(sense.nikud) !== sense.headword" class="entry-hw">
        {{ sense.headword }}
      </span>
      <span v-if="sense.pos" class="entry-pos">{{ sense.pos }}</span>
      <span v-if="sense.binyan" class="entry-binyan">{{ sense.binyan }}</span>
    </div>

    <!-- שורש -->
    <div v-if="sense.shoresh" class="entry-shoresh">
      שורש: <span class="shoresh-val">{{ sense.shoresh }}</span>
    </div>

    <!-- Etymology (=expansion) — extracted at import from Aramaic entries -->
    <div v-if="sense.etymology" class="entry-etymology">
      מקור: <span class="etymology-val">{{ sense.etymology }}</span>
    </div>

    <!-- Definitions -->
    <div v-if="sense.definitions.length" class="entry-defs">
      <div v-for="(def, di) in sense.definitions" :key="di" class="def-row">
        <span v-if="sense.definitions.length > 1" class="def-num">{{ di + 1 }}.</span>
        <span class="def-text">{{ maybeFilter(def.text) }}</span>
        <div v-if="def.examples.length" class="def-examples">
          <div v-for="(ex, ei) in def.examples" :key="ei" class="def-example">
            <span class="ex-text">{{ maybeFilter(ex.text) }}</span>
            <span v-if="ex.source" class="ex-source">({{ ex.source }})</span>
          </div>
        </div>
      </div>
    </div>

    <!-- Sections -->
    <template v-for="secKey in SECTION_ORDER" :key="secKey">
      <div v-if="sense.sections[secKey]?.length" class="entry-section">
        <span class="sec-label">{{ secKey }}</span>
        <div class="sec-body">
          <template v-for="(item, ii) in sense.sections[secKey]" :key="ii">
            <button
              v-if="
                secKey === 'מילים נרדפות' ||
                secKey === 'ניגודים' ||
                secKey === 'ראו גם' ||
                secKey === 'נגזרות'
              "
              class="sec-link"
              @click="$emit('searchWord', item)"
            >
              {{ item }}
            </button>
            <span v-else class="sec-text">{{ maybeFilter(item) }}</span>
            {{ ii < (sense.sections[secKey]?.length ?? 0) - 1 ? '\u00A0' : '' }}
          </template>
        </div>
      </div>
    </template>

  </div>
</template>

<style scoped>
.entry {
  display: flex;
  flex-direction: column;
  gap: 8px;
  direction: rtl;
  user-select: text;
}

/* ── Head ── */
.entry-head {
  display: flex;
  align-items: baseline;
  gap: 6px;
  padding-bottom: 4px;
  border-bottom: 1px solid var(--accent-color);
  flex-wrap: wrap;
}
.entry-nikud {
  font-size: 18px;
  font-weight: 700;
  color: var(--text-primary);
  line-height: 1.2;
}
.entry-hw {
  font-size: 12px;
  color: var(--text-secondary);
}
.entry-idx {
  font-size: 14px;
  font-weight: 700;
  color: var(--text-secondary);
  flex-shrink: 0;
  min-width: 18px;
}
.entry-pos {
  font-size: 11px;
  color: var(--accent-color);
  background: color-mix(in srgb, var(--accent-color) 10%, transparent);
  border-radius: 999px;
  padding: 0 6px;
  line-height: 16px;
}
.entry-binyan {
  font-size: 11px;
  color: var(--text-secondary);
  border: 1px solid var(--border-color);
  border-radius: 999px;
  padding: 0 6px;
  line-height: 16px;
}

/* ── Source label (Aramaic DB entries) ── */
.entry-source {
  font-size: 10px;
  color: var(--text-secondary);
  background: color-mix(in srgb, var(--text-secondary) 10%, transparent);
  border-radius: 999px;
  padding: 0 6px;
  line-height: 16px;
  margin-inline-start: auto;
}

/* ── שורש ── */
.entry-shoresh {
  font-size: 12px;
  color: var(--text-secondary);
  padding-inline-start: 8px;
}
.shoresh-val {
  font-weight: 600;
  color: var(--text-primary);
  letter-spacing: 0.05em;
}

/* ── Etymology ── */
.entry-etymology {
  font-size: 12px;
  color: var(--text-secondary);
  padding-inline-start: 8px;
}
.etymology-val {
  font-weight: 600;
  color: var(--text-primary);
}

/* ── Definitions ── */
.entry-defs {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding-inline-start: 8px;
  border-inline-start: 2px solid color-mix(in srgb, var(--border-color) 70%, transparent);
}
.def-row {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 4px;
}
.def-num {
  font-size: 10px;
  color: var(--text-secondary);
  flex-shrink: 0;
  min-width: 12px;
}
.def-layer {
  font-size: 10px;
  color: var(--accent-color);
  background: color-mix(in srgb, var(--accent-color) 10%, transparent);
  border-radius: 999px;
  padding: 0 5px;
  line-height: 15px;
  flex-shrink: 0;
}
.def-text {
  font-size: 13px;
  color: var(--text-primary);
  line-height: 1.5;
}
.def-examples {
  width: 100%;
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding-inline-start: 14px;
  margin-top: 2px;
}
.def-example {
  font-size: 11px;
  color: var(--text-secondary);
  line-height: 1.5;
  font-style: italic;
}
.ex-source {
  font-size: 10px;
  opacity: 0.7;
  margin-inline-start: 4px;
}

/* ── Sections ── */
.entry-section {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding-inline-start: 8px;
}
.sec-label {
  font-size: 10px;
  font-weight: 600;
  color: var(--text-secondary);
  letter-spacing: 0.03em;
}
.sec-body {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  font-size: 12px;
  color: var(--text-secondary);
  line-height: 1.6;
}
.sec-body::after {
  content: '';
  flex: 1 0 0;
}
.sec-link {
  display: inline;
  color: var(--accent-color);
  font-size: 12px;
  font-weight: 600;
  text-decoration: underline;
  text-underline-offset: 2px;
  border: none;
  background: none;
  cursor: pointer;
  padding: 0;
  border-radius: 0;
}
.sec-text {
  font-size: 12px;
  color: var(--text-primary);
}

/* ── Translations ── */
.trans-item {
  font-size: 12px;
  color: var(--text-primary);
  margin-inline-end: 10px;
}
.trans-lang {
  color: var(--text-secondary);
  margin-inline-end: 3px;
}
</style>
