<script setup lang="ts">
import HintIcon from '@/components/HintIcon.vue'
defineProps<{ label?: string; wrap?: boolean; hint?: string }>()
</script>

<template>
  <div class="setting-row setting-row-item" :class="{ wrap }">
    <span v-if="label" class="setting-label">
      {{ label }}<HintIcon v-if="hint" :hint="hint" />
    </span>
    <div class="setting-control">
      <slot />
      <!-- A row whose control speaks for itself needs no label, but may still
           carry a hint — keep it reachable next to the control. Absolutely
           positioned so it takes no width: otherwise this row's control is
           ~20px narrower than its siblings' and its toggle grid resolves to
           different track sizes than theirs. -->
      <HintIcon v-if="hint && !label" class="control-hint" :hint="hint" />
    </div>
  </div>
</template>

<style scoped>
.setting-row {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-bottom: 16px;
}
/* Zero-specificity default so the row is self-sufficient outside the settings
   page (setup wizard); SettingsPage.vue's [data-section] rule overrides it. */
:where(.setting-label) {
  font-size: 11px;
  color: var(--text-secondary);
}
.setting-label {
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
  position: relative;
}
.control-hint {
  position: absolute;
  inset-inline-end: 0;
  top: 50%;
  translate: 0 -50%;
  margin: 0;
}
.wrap .setting-control {
  flex-wrap: wrap;
}
</style>
