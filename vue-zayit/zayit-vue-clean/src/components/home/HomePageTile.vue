<script setup lang="ts">
import type { Component } from 'vue'
defineProps<{ label: string; icon: Component; color: string }>()
defineEmits<{ tap: [] }>()
</script>

<template>
  <button class="tile" :style="{ '--c': color }" @click="$emit('tap')">
    <div class="tile-icon"><component :is="icon" /></div>
    <span class="tile-label">{{ label }}</span>
  </button>
</template>

<style scoped>
.tile {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  width: 80px;
  padding: 8px 4px;
  background: none;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
  transition: background 0.1s;
}
.tile:hover { background: var(--hover-bg); }
.tile:active { background: var(--active-bg); }

.tile-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 56px;
  height: 56px;
  border-radius: 12px;
  background: color-mix(in srgb, var(--c) 15%, transparent);
  color: var(--c);
  font-size: 28px;
}

.tile-label {
  font-size: 12px;
  color: var(--text-primary);
  text-align: center;
  line-height: 1.3;
  max-width: 76px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
