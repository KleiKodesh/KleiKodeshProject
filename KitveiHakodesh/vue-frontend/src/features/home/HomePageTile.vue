<script setup lang="ts">
import type { Component } from 'vue'
import { IconPin12Filled } from '@iconify-prerendered/vue-fluent'
defineProps<{
  label: string
  icon: Component
  color?: string
  iconScale?: number
  pinned?: boolean
}>()
defineEmits<{ tap: [] }>()
</script>

<template>
  <button class="tile" data-nav-item title="לחץ Tab למעבר בין האפשרויות, Enter לפתיחה" @click="$emit('tap')">
    <div class="tile-icon">
      <component
        :is="icon"
        :style="{ ...(color ? { color } : {}), ...(iconScale !== undefined ? { fontSize: iconScale + 'em' } : {}) }"
      />
      <span v-if="pinned" class="tile-pin" aria-label="מוצמד"><IconPin12Filled /></span>
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
}
.tile:focus-visible {
  outline: none;
}
.tile:focus-visible .tile-icon {
  transform: scale(1.25);
}
.tile:hover .tile-icon {
  transform: scale(1.15);
}
.tile:active .tile-icon {
  transform: scale(0.95);
}

.tile-icon {
  position: relative;
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

.tile-pin {
  position: absolute;
  top: -3px;
  inset-inline-start: -3px;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 15px;
  height: 15px;
  border-radius: 50%;
  background: var(--bg-primary);
  color: var(--accent-color);
  font-size: 10px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.18);
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
