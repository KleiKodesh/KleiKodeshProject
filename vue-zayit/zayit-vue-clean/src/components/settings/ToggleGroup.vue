<script setup lang="ts" generic="T extends string | boolean">
defineProps<{ options: { label: string; value: T }[]; modelValue: T }>()
defineEmits<{ 'update:modelValue': [T] }>()
</script>

<template>
  <button
    v-for="opt in options"
    :key="String(opt.value)"
    :class="['toggle-btn', { active: modelValue === opt.value }]"
    @click="$emit('update:modelValue', opt.value)"
  >{{ opt.label }}</button>
</template>

<style scoped>
.toggle-btn {
  flex: 1;
  padding: 5px 10px;
  border: 1px solid var(--border-color);
  background: var(--bg-secondary);
  color: var(--text-primary);
  cursor: pointer;
  font-size: 12px;
  white-space: nowrap;
  border-radius: 4px;
}
.toggle-btn:hover { background: var(--hover-bg); }
.toggle-btn.active { background: var(--accent-color); color: white; border-color: var(--accent-color); }
</style>
