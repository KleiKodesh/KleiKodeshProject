<script setup lang="ts">
import HintIcon from './HintIcon.vue'
defineProps<{ label?: string; wrap?: boolean; hint?: string }>()
</script>

<template>
  <div class="setting-row" :class="{ wrap }">
    <span v-if="label" class="setting-label">
      {{ label }}<HintIcon v-if="hint" :hint="hint" />
    </span>
    <div class="setting-control">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.setting-row {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-bottom: 10px;
}
.setting-label {
  font-size: 11px;
  color: var(--text-secondary);
  display: flex;
  align-items: center;
  gap: 4px;
  cursor: default;
}
.setting-control {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: nowrap;
  width: 100%;
}
.wrap .setting-control {
  flex-wrap: wrap;
}
</style>
