<script setup lang="ts">
import { IconInfo16Regular } from '@iconify-prerendered/vue-fluent'
defineProps<{ label?: string; wrap?: boolean; hint?: string }>()
</script>

<template>
  <div class="setting-row" :class="{ wrap }">
    <span v-if="label" class="setting-label" :title="hint">
      {{ label }}<IconInfo16Regular v-if="hint" class="hint-icon" />
    </span>
    <div class="setting-control">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.setting-row { display: flex; flex-direction: column; gap: 4px; margin-bottom: 10px; }
.setting-label { font-size: 11px; color: var(--text-secondary); display: flex; align-items: center; gap: 4px; cursor: default; }
.hint-icon { opacity: 0.5; flex-shrink: 0; }
.setting-control { display: flex; align-items: center; gap: 6px; flex-wrap: nowrap; width: 100%; }
.wrap .setting-control { flex-wrap: wrap; }
</style>
