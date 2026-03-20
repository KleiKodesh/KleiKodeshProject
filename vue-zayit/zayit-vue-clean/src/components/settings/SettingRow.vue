<script setup lang="ts">
defineProps<{ label?: string; wrap?: boolean }>()
</script>

<template>
  <div class="setting-row" :class="{ wrap }">
    <span v-if="label" class="setting-label">{{ label }}</span>
    <div class="setting-control">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.setting-row { display: flex; flex-direction: column; gap: 4px; margin-bottom: 10px; }
.setting-label { font-size: 11px; color: var(--text-secondary); }
.setting-control { display: flex; align-items: center; gap: 4px; flex-wrap: nowrap; }
.wrap .setting-control { flex-wrap: wrap; }
</style>
