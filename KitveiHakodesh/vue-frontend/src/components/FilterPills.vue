<script setup lang="ts">
// Filter pills: an "all" pill plus one toggle pill per item. Nothing selected = all.
// Each row stretches its pills to fill the width, so wrapped rows end flush.
export interface FilterPillItem {
  key: string
  label: string
  title?: string
}

const props = defineProps<{
  items: FilterPillItem[]
  allLabel: string
  itemTestId?: string
}>()
const selected = defineModel<string[]>({ required: true })

function toggle(key: string) {
  selected.value = selected.value.includes(key)
    ? selected.value.filter((k) => k !== key)
    : [...selected.value, key]
}
</script>

<template>
  <div class="filter-pills" role="group">
    <button
      class="filter-pill"
      :class="{ active: !selected.length }"
      @mousedown.prevent
      @click="selected = []"
    >{{ allLabel }}</button>
    <button
      v-for="item in props.items"
      :key="item.key"
      class="filter-pill"
      :class="{ active: selected.includes(item.key) }"
      :title="item.title ?? item.label"
      :data-testid="itemTestId"
      @mousedown.prevent
      @click="toggle(item.key)"
    >{{ item.label }}</button>
  </div>
</template>

<style scoped>
.filter-pills {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.filter-pill {
  flex: 1 1 auto;
  min-width: 0;
  max-width: 260px;
  height: 24px;
  padding: 0 12px;
  font-size: 12px;
  font-family: inherit;
  line-height: 1;
  border: none;
  border-radius: 999px;
  background: var(--bg-secondary);
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  cursor: pointer;
}
.filter-pill:hover { background: var(--hover-bg); color: var(--text-primary); }
/* Same active look as the toolbar / commentary-nav toggle buttons. */
.filter-pill.active,
.filter-pill.active:hover {
  background: var(--active-bg);
  color: var(--text-primary);
}
</style>
