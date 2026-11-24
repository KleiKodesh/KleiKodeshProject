<script setup lang="ts">
import { IconInfo16Regular } from '@iconify-prerendered/vue-fluent'
defineProps<{ hint: string }>()
</script>

<template>
  <span class="hint-icon" :title="hint">
    <IconInfo16Regular />
  </span>
</template>

<style scoped>
.hint-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  opacity: 0.5;
  flex-shrink: 0;
  cursor: help;
  padding: 8px;
  margin: -8px;
}
.hint-icon:hover {
  opacity: 0.9;
}
</style>
