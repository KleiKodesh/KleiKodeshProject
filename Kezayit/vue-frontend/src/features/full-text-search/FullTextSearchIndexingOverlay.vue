<script setup lang="ts">
import type { IndexingState } from './useFullTextSearchIndexingStatus'
defineProps<{ state: IndexingState }>()
</script>

<template>
  <div class="indexing-banner">
    <div class="banner-top">
      <span class="status-message">
        <template v-if="!state.isReady && state.isIndexing && state.percentage >= 100">מסיים בניית האינדקס…</template>
        <template v-else-if="!state.isReady">אנא המתן בעת בניית האינדקס</template>
        <template v-else>תוצאות חיפוש חלקיות — האינדקס עדיין בבנייה</template>
      </span>
      <span class="percentage">{{ Math.round(state.percentage) }}%<span v-if="state.eta"> · {{ state.eta }}</span></span>
    </div>
    <div class="progress-track">
      <div class="progress-fill" :style="{ width: `${state.percentage}%` }" />
      <div
        v-if="state.latestSegmentPct !== null"
        class="segment-marker"
        :style="{ left: `${100 - state.latestSegmentPct}%` }"
      />
    </div>
  </div>
</template>

<style scoped>
.indexing-banner {
  display: flex;
  flex-direction: column;
  gap: 5px;
  padding: 6px 12px 5px;
  background: var(--bg-secondary);
  border-top: 1px solid var(--border-color);
  flex-shrink: 0;
}
.banner-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.status-message {
  font-size: 11px;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.percentage {
  font-size: 10px;
  color: var(--text-secondary);
  white-space: nowrap;
  flex-shrink: 0;
}
.progress-track {
  position: relative;
  width: 100%;
  height: 4px;
  border-radius: 2px;
  background: color-mix(in srgb, var(--text-secondary) 20%, transparent);
  overflow: visible;
  direction: ltr;
}
.progress-fill {
  height: 100%;
  border-radius: 2px;
  background: var(--accent-color);
  transition: width 0.4s ease;
  margin-inline-start: auto;
}
.segment-marker {
  position: absolute;
  top: 50%;
  transform: translate(50%, -50%);
  width: 3px;
  height: 10px;
  border-radius: 1px;
  background: var(--bg-secondary);
  pointer-events: none;
}
</style>
