<script setup lang="ts">
import { computed } from 'vue'
import type { DictEntryContent } from './useDictionarySearch'

const props = defineProps<{ entry: DictEntryContent }>()

interface Sense {
  nikud: string | null
  text: string
}

/**
 * Parse the definition string into one or more senses.
 * Handles:
 *   - *** separator between multiple forms/meanings
 *   - {nikud} at the start of each segment
 *   - (=...) abbreviation expansions
 *   - Raw HTML from book entries (passed through as-is)
 */
const senses = computed<Sense[]>(() => {
  const raw = props.entry.html

  // Book entries contain HTML — render as-is, single sense
  if (raw.includes('<') && raw.includes('>')) {
    return [{ nikud: props.entry.nikud, text: raw.replace(/\n/g, '<br>') }]
  }

  // Split on *** to get multiple senses
  const parts = raw
    .split('***')
    .map((s) => s.trim())
    .filter(Boolean)

  return parts.map((part) => {
    // Each part may start with {nikud}
    const nikudMatch = part.match(/^\{([^}]+)\}\s*(.*)$/)
    if (nikudMatch) {
      return { nikud: nikudMatch[1], text: nikudMatch[2].trim() }
    }
    // First part uses the entry-level nikud if no inline nikud
    return { nikud: null, text: part }
  })
})

// For abbreviations: extract the (=expansion) if present
function formatAbbrevExpansion(text: string): { expansion: string | null; rest: string } {
  const m = text.match(/^\(=([^)]+)\)\s*(.*)$/)
  if (m) return { expansion: m[1], rest: m[2].trim() }
  return { expansion: null, rest: text }
}
</script>

<template>
  <div class="entry-view">
    <!-- Senses -->
    <div class="entry-body">
      <div v-for="(sense, i) in senses" :key="i" class="entry-sense">
        <!-- Sense nikud (for *** segments with their own form) -->
        <span v-if="sense.nikud && sense.nikud !== entry.nikud" class="sense-nikud">
          {{ sense.nikud }}
        </span>

        <!-- Abbreviation expansion -->
        <template v-if="entry.type === 'abbrev'">
          <span v-if="formatAbbrevExpansion(sense.text).expansion" class="sense-expansion">
            {{ formatAbbrevExpansion(sense.text).expansion }}
          </span>
          <span v-if="formatAbbrevExpansion(sense.text).rest" class="sense-text">
            {{ formatAbbrevExpansion(sense.text).rest }}
          </span>
        </template>

        <!-- Regular definition or HTML content -->
        <span v-else-if="sense.text.includes('<')" class="sense-html" v-html="sense.text" />
        <span v-else class="sense-text">{{ sense.text }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.entry-view {
  display: flex;
  flex-direction: column;
  height: 100%;
  direction: rtl;
}

/* ── Senses ── */
.entry-body {
  flex: 1;
  overflow-y: auto;
  padding: 6px 10px;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  display: flex;
  flex-direction: column;
  gap: 0;
}

.entry-sense {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0;
  padding: 4px 0;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 40%, transparent);
}

.entry-sense:last-child {
  border-bottom: none;
  padding-bottom: 0;
}

.sense-nikud {
  font-size: 13px;
  font-weight: 600;
  color: var(--accent-color);
}

.sense-expansion {
  font-size: 12px;
  color: var(--text-secondary);
  font-style: italic;
}

.sense-text {
  font-size: 13px;
  color: var(--text-primary);
  line-height: 1.5;
  text-align: justify;
}

.sense-html {
  font-size: 13px;
  color: var(--text-primary);
  line-height: 1.5;
  text-align: justify;
}

.sense-html :deep(b) {
  font-weight: 700;
}
.sense-html :deep(big) {
  font-size: 1.1em;
  font-weight: 700;
  color: var(--accent-color);
}
.sense-html :deep(h3) {
  font-size: 1.1em;
  font-weight: 700;
  color: var(--accent-color);
  margin: 0 0 4px;
}
.sense-html :deep(small) {
  font-size: 0.85em;
  color: var(--text-secondary);
}
.sense-html :deep(span[dir='ltr']) {
  direction: ltr;
  display: inline-block;
  color: var(--text-secondary);
  font-style: italic;
}
</style>
