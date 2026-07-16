<script setup lang="ts">
import type { ZmanRow } from './useNextZman'

defineProps<{
  rows: ZmanRow[]
  cityName: string
}>()
</script>

<template>
  <div class="zman-popup" role="dialog" aria-label="זמני היום">
    <div class="zp-head">
      <span class="zp-title">זמני היום</span>
      <span class="zp-city">{{ cityName }}</span>
    </div>
    <div class="zp-grid">
      <template v-for="r in rows" :key="r.key">
        <span class="zp-label" :class="{ next: r.isNext, passed: r.passed }">{{ r.label }}</span>
        <span class="zp-value" :class="{ next: r.isNext, passed: r.passed }">{{ r.time }}</span>
      </template>
    </div>
  </div>
</template>

<style scoped>
.zman-popup {
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 6px 24px rgba(0, 0, 0, 0.28);
  padding: 8px 10px 10px;
  min-width: 220px;
  direction: rtl;
  max-height: inherit;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}
.zp-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 8px;
  padding: 0 2px 6px;
  margin-bottom: 4px;
  border-bottom: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
}
.zp-title {
  font-size: 12px;
  font-weight: 700;
  color: var(--text-primary);
}
.zp-city {
  font-size: 11px;
  color: var(--text-secondary);
}
.zp-grid {
  display: grid;
  grid-template-columns: 1fr auto;
  column-gap: 16px;
}
.zp-label,
.zp-value {
  font-size: 12px;
  padding: 2px 2px;
  line-height: 1.4;
}
.zp-label {
  color: var(--text-secondary);
}
.zp-value {
  font-weight: 600;
  color: var(--text-primary);
  font-variant-numeric: tabular-nums;
  text-align: start;
}
.zp-label.passed,
.zp-value.passed {
  opacity: 0.45;
}
.zp-label.next,
.zp-value.next {
  opacity: 1;
  color: var(--accent-color, #0078d4);
  font-weight: 700;
}
</style>
