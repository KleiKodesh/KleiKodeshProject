<script setup lang="ts">
import type { IndexingState } from './useIndexingStatus'
defineProps<{ state: IndexingState }>()
</script>

<template>
  <div class="overlay">
    <div class="card">
      <div class="ring-wrap">
        <svg viewBox="0 0 48 48" width="64" height="64" style="transform: rotate(-90deg)">
          <circle
            cx="24"
            cy="24"
            r="20"
            fill="none"
            stroke-width="3"
            stroke="color-mix(in srgb, var(--text-secondary) 20%, transparent)"
          />
          <circle
            cx="24"
            cy="24"
            r="20"
            fill="none"
            stroke-width="3"
            stroke="var(--accent-color)"
            stroke-linecap="round"
            :stroke-dasharray="`${(state.percentage / 100) * 125.66} 125.66`"
            style="transition: stroke-dasharray 0.4s ease"
          />
        </svg>
        <span class="pct">{{ Math.round(state.percentage) }}%</span>
      </div>
      <p class="title">בונה אינדקס חיפוש</p>
      <p class="sub">
        {{ state.processedChunks }} / {{ state.totalChunks }} קטעים<span v-if="state.eta">
          · {{ state.eta }}</span
        >
      </p>
      <p class="note">ניתן לחפש לאחר סיום הבנייה</p>
    </div>
  </div>
</template>

<style scoped>
.overlay {
  position: absolute;
  inset: 0;
  z-index: 20;
  display: flex;
  align-items: center;
  justify-content: center;
  background: color-mix(in srgb, var(--bg-primary) 85%, transparent);
  backdrop-filter: blur(4px);
}
.card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  padding: 28px 32px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  min-width: 220px;
  text-align: center;
}
.ring-wrap {
  position: relative;
  width: 64px;
  height: 64px;
  display: flex;
  align-items: center;
  justify-content: center;
}
.pct {
  position: absolute;
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
}
.title {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0;
}
.sub {
  font-size: 12px;
  color: var(--text-secondary);
  margin: 0;
}
.note {
  font-size: 11px;
  color: var(--text-secondary);
  opacity: 0.7;
  margin: 0;
}
</style>
