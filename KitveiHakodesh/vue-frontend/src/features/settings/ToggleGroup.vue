<script setup lang="ts" generic="T extends string | boolean">
defineProps<{ options: { label: string; value: T }[]; modelValue: T }>()
defineEmits<{ 'update:modelValue': [T] }>()
</script>

<template>
  <div class="toggle-group">
    <button
      v-for="opt in options"
      :key="String(opt.value)"
      :class="['toggle-btn', { active: modelValue === opt.value }]"
      :title="opt.label"
      @click="$emit('update:modelValue', opt.value)"
    >
      {{ opt.label }}
    </button>
  </div>
</template>

<style scoped>
.toggle-group {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(130px, 1fr));
  gap: 4px;
  width: 100%;
}

.toggle-btn {
  height: 28px;
  padding: 0 10px;
  border: 1px solid var(--border-color);
  background: var(--bg-secondary);
  color: var(--text-primary);
  cursor: pointer;
  font-size: 12px;
  border-radius: 4px;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}
.toggle-btn:hover {
  background: var(--hover-bg);
}
.toggle-btn.active {
  background: var(--accent-color);
  color: white;
  border-color: var(--accent-color);
}
</style>
