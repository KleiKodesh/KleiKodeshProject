<script setup lang="ts">
import { computed } from 'vue'
import type { DictEntryContent } from './useDictionarySearch'

const props = defineProps<{ entry: DictEntryContent }>()

interface Sense {
  nikud: string | null
  text: string
  expansion: string | null
}

/** Parse *** separated senses, extracting inline {nikud} and (=expansion) */
const senses = computed<Sense[]>(() => {
  const raw = props.entry.html

  // Book entries contain HTML — single sense, render as-is
  if (raw.includes('<') && raw.includes('>')) {
    return [{ nikud: props.entry.nikud, text: raw.replace(/\n/g, '<br>'), expansion: null }]
  }

  const parts = raw
    .split('***')
    .map((s) => s.trim())
    .filter(Boolean)

  return parts.map((part) => {
    const nikudMatch = part.match(/^\{([^}]+)\}\s*(.*)$/)
    const text = nikudMatch ? (nikudMatch[2] ?? '').trim() : part
    const nikud = nikudMatch ? (nikudMatch[1] ?? null) : null

    const abbrevMatch = text.match(/^\(=([^)]+)\)\s*(.*)$/)
    return {
      nikud,
      text: abbrevMatch ? (abbrevMatch[2] ?? '').trim() : text,
      expansion: abbrevMatch ? (abbrevMatch[1] ?? null) : null,
    }
  })
})

/** "ראה גם: X" cross-reference entries */
const crossRef = computed(() => {
  const raw = props.entry.html.trim()
  const m = raw.match(/^ראה גם:\s*(.+)$/)
  return m ? (m[1] ?? '').trim() : null
})

const isHtml = computed(() => props.entry.html.includes('<') && props.entry.html.includes('>'))
</script>

<template>
  <div class="entry-view">
    <!-- Cross-reference (ראה גם) -->
    <div v-if="crossRef" class="entry-crossref">
      <span class="crossref-label">ראה גם:</span>
      <span class="crossref-target">{{ crossRef }}</span>
    </div>

    <!-- Abbreviation expansion -->
    <div v-else-if="entry.type === 'abbrev'" class="entry-abbrev">
      <div v-for="(sense, i) in senses" :key="i" class="abbrev-sense">
        <span v-if="sense.expansion" class="abbrev-expansion">{{ sense.expansion }}</span>
        <span v-if="sense.text" class="abbrev-rest">{{ sense.text }}</span>
      </div>
    </div>

    <!-- HTML book entry -->
    <div v-else-if="isHtml" class="entry-html-body">
      <div v-for="(sense, i) in senses" :key="i" class="sense-html" v-html="sense.text" />
    </div>

    <!-- Regular senses (aramaic / wiktionary) -->
    <div v-else class="entry-senses">
      <div v-for="(sense, i) in senses" :key="i" class="entry-sense">
        <span v-if="senses.length > 1" class="sense-num">{{ i + 1 }}.</span>
        <span v-if="sense.nikud && sense.nikud !== entry.nikud" class="sense-nikud">
          {{ sense.nikud }}
        </span>
        <span class="sense-text">{{ sense.text }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.entry-view {
  padding: 6px 10px 8px;
  direction: rtl;
}

/* ── Cross-reference ── */
.entry-crossref {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  color: var(--text-secondary);
}
.crossref-label {
  color: var(--text-secondary);
}
.crossref-target {
  color: var(--accent-color);
  font-weight: 600;
}

/* ── Abbreviation ── */
.entry-abbrev {
  display: flex;
  flex-direction: column;
  gap: 3px;
}
.abbrev-sense {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 4px;
}
.abbrev-expansion {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
}
.abbrev-rest {
  font-size: 12px;
  color: var(--text-secondary);
}

/* ── HTML book entry ── */
.entry-html-body {
  font-size: 13px;
  color: var(--text-primary);
  line-height: 1.55;
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
  font-size: 1.05em;
  font-weight: 700;
  color: var(--accent-color);
  margin: 0 0 3px;
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

/* ── Regular senses ── */
.entry-senses {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.entry-sense {
  display: flex;
  align-items: baseline;
  gap: 4px;
  flex-wrap: wrap;
}
.sense-num {
  font-size: 11px;
  color: var(--text-secondary);
  flex-shrink: 0;
  min-width: 14px;
}
.sense-nikud {
  font-size: 12px;
  font-weight: 600;
  color: var(--accent-color);
  flex-shrink: 0;
}
.sense-text {
  font-size: 13px;
  color: var(--text-primary);
  line-height: 1.5;
}
</style>
