<script setup lang="ts">
import type { Component } from 'vue'
defineProps<{
  label: string
  icon: Component
  color?: string
  isFocused?: boolean
}>()
defineEmits<{ tap: [] }>()
</script>

<template>
  <button class="tile" data-nav-item :class="{ 'is-focused': isFocused }" @click="$emit('tap')">
    <div class="tile-icon">
      <component :is="icon" :style="color ? { color } : {}" />
    </div>
    <span class="tile-label">{{ label }}</span>
  </button>
</template>

<style scoped>
.tile {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 5px;
  width: 72px;
  padding: 6px 4px;
  background: none;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
  transition: background 120ms;
}
.tile:focus-visible {
  outline: none;
}
.tile.is-focused {
  background: var(--hover-bg);
}
.tile:active {
  background: var(--active-bg);
}
.tile:hover .tile-icon {
  transform: scale(1.08);
}
.tile:active .tile-icon {
  transform: scale(0.95);
}

.tile-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 48px;
  height: 48px;
  border-radius: 6px;
  background: none;
  font-size: 28px;
  transition: transform 0.15s ease;
}

.tile-label {
  font-size: 11px;
  color: var(--text-primary);
  text-align: center;
  line-height: 1.3;
  max-width: 68px;
  overflow: hidden;
  white-space: normal;
  word-break: break-word;
}
</style>
