<script setup lang="ts">
export interface TabItem {
  key: string
  label: string
}

const props = defineProps<{
  tabs: TabItem[]
  modelValue: string
}>()

const emit = defineEmits<{ 'update:modelValue': [key: string] }>()

function activeIndex(): number {
  return props.tabs.findIndex((t) => t.key === props.modelValue)
}
</script>

<template>
  <div class="tab-strip" :style="{ '--tab-index': activeIndex(), '--tab-count': tabs.length }">
    <button
      v-for="tab in tabs"
      :key="tab.key"
      class="tab-btn"
      :class="{ active: modelValue === tab.key }"
      @click="emit('update:modelValue', tab.key)"
    >
      {{ tab.label }}
    </button>
  </div>
</template>

<style scoped>
.tab-strip {
  display: flex;
  flex-shrink: 0;
  border-bottom: 1px solid var(--border-color);
  background: var(--bg-toolbar);
  position: relative;
}
.tab-strip::after {
  content: '';
  position: absolute;
  bottom: 0;
  height: 2px;
  background: var(--accent-color);
  width: calc(100% / var(--tab-count));
  /* RTL: tab 0 is on the right, index 0 = right edge */
  right: calc(var(--tab-index) * 100% / var(--tab-count));
  transition: right 0.2s cubic-bezier(0.4, 0, 0.2, 1);
}
.tab-btn {
  flex: 1;
  height: 32px;
  padding: 0 12px;
  font-size: 12px;
  color: var(--text-secondary);
  background: transparent;
  border: none;
  border-bottom: 2px solid transparent;
  border-radius: 0;
  cursor: pointer;
}
.tab-btn:hover {
  background: var(--hover-bg);
  color: var(--text-primary);
}
.tab-btn:active {
  transform: none;
}
.tab-btn.active {
  color: var(--text-primary);
  border-bottom-color: transparent;
}
</style>
