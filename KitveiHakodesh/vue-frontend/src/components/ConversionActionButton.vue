<script setup lang="ts">
import { IconDismiss20Regular, IconCheckmark20Regular } from '@iconify-prerendered/vue-fluent'

/**
 * One button for a long conversion (Word -> PDF, export to Word):
 *  - idle: the caller's icon (slot); a click starts the work;
 *  - busy: an animated X - a small X inside a turning arc - and a click cancels;
 *  - done: a green check; a click opens the result.
 * The button keeps the same size and hit area in every state; only the icon changes.
 */
export type ConversionActionState = 'idle' | 'busy' | 'done'

defineProps<{
  state: ConversionActionState
  idleTitle: string
  busyTitle: string
  doneTitle?: string
  /** Text shown after the icon (the conversion card's cancel button); none on a toolbar. */
  label?: string
}>()

defineEmits<{ start: []; cancel: []; open: [] }>()
</script>

<template>
  <button
    class="conversion-action-button"
    :class="`conversion-action-button--${state}`"
    :title="state === 'busy' ? busyTitle : state === 'done' ? (doneTitle ?? idleTitle) : idleTitle"
    :aria-busy="state === 'busy'"
    :data-state="state"
    @click="state === 'busy' ? $emit('cancel') : state === 'done' ? $emit('open') : $emit('start')"
  >
    <span v-if="state === 'busy'" class="conversion-action-icon conversion-action-busy">
      <span class="conversion-action-ring" />
      <IconDismiss20Regular class="conversion-action-x" />
    </span>
    <IconCheckmark20Regular v-else-if="state === 'done'" class="conversion-action-icon conversion-action-check" />
    <span v-else class="conversion-action-icon"><slot /></span>
    <span v-if="label" class="conversion-action-label">{{ label }}</span>
  </button>
</template>

<style scoped>
.conversion-action-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
}
.conversion-action-icon {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  height: 16px;
  flex-shrink: 0;
}
.conversion-action-icon :deep(svg) {
  width: 16px;
  height: 16px;
}
/* busy: a small X with an arc turning around it - the progress */
.conversion-action-busy .conversion-action-x {
  width: 9px;
  height: 9px;
}
.conversion-action-ring {
  position: absolute;
  inset: 0;
  border: 2px solid currentColor;
  border-top-color: transparent;
  border-radius: 50%;
  opacity: 0.85;
  animation: conversion-action-spin 0.8s linear infinite;
}
@keyframes conversion-action-spin {
  to { transform: rotate(360deg); }
}
.conversion-action-check {
  color: var(--status-success);
}
@media (prefers-reduced-motion: reduce) {
  .conversion-action-ring { animation-duration: 2.4s; }
}
</style>
