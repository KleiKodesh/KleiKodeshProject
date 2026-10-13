<script setup lang="ts">
import { useHomeDailyLearning } from './useHomeDailyLearning'

const { cards, openCard } = useHomeDailyLearning()
</script>

<template>
  <div v-if="cards.length" class="daily-learning" dir="rtl">
    <button
      v-for="card in cards"
      :key="card.key"
      class="daily-learning__card"
      type="button"
      :title="card.location"
      @click="openCard(card)"
    >
      <span class="daily-learning__label">{{ card.label }}</span>
      <span class="daily-learning__location">{{ card.location }}</span>
    </button>
  </div>
</template>

<style scoped>
.daily-learning {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 6px 8px;
  width: 100%;
  margin-top: 12px;
}

.daily-learning__card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
  min-width: 96px;
  max-width: 180px;
  padding: 6px 12px;
  background: none;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  color: var(--text-primary);
  cursor: pointer;
  transition:
    background 150ms,
    transform 0.15s ease;
  -webkit-tap-highlight-color: transparent;
}

.daily-learning__card:hover {
  background: var(--input-bg-focus);
}

.daily-learning__card:focus-visible {
  outline: 2px solid var(--border-color);
  outline-offset: 1px;
}

.daily-learning__card:active {
  transform: scale(0.97);
}

.daily-learning__label {
  font-size: 11px;
  color: var(--text-secondary);
}

.daily-learning__location {
  max-width: 100%;
  font-size: 13px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: clip;
}
</style>
