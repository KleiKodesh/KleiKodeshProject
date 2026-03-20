<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue'
import { onClickOutside } from '@vueuse/core'
import type { CommentaryGroup } from './useCommentary'

const props = defineProps<{
  groups: CommentaryGroup[]
  ctLabels: Record<string, string>
}>()

const emit = defineEmits<{
  'navigate': [bookId: number]
  'close': []
}>()

const dropdownEl = ref<HTMLElement | null>(null)

// Unique primary connection types in order (first CT of each group = primary)
const connectionTypes = computed(() => {
  const seen = new Set<string>()
  for (const g of props.groups) {
    if (g.connectionTypes[0]) seen.add(g.connectionTypes[0])
  }
  return [...seen]
})

onMounted(() => nextTick(() => {
  // Clamp right edge to viewport
  if (!dropdownEl.value) return
  const rect = dropdownEl.value.getBoundingClientRect()
  if (rect.right > window.innerWidth - 8) {
    dropdownEl.value.style.left = 'auto'
    dropdownEl.value.style.right = '0'
  }
}))

onClickOutside(dropdownEl, () => emit('close'))

function navigateToCt(ct: string) {
  // Navigate to first group whose primary CT matches
  const firstGroup = props.groups.find(g => g.connectionTypes[0] === ct)
  if (firstGroup) emit('navigate', firstGroup.bookId)
}
</script>

<template>
  <div ref="dropdownEl" class="ct-dropdown">
    <button
      v-for="ct in connectionTypes"
      :key="ct"
      class="ct-dropdown-item c-pointer hover-bg"
      @click.stop="navigateToCt(ct)"
    >
      {{ ctLabels[ct] ?? ct }}
    </button>
  </div>
</template>

<style scoped>
.ct-dropdown {
  position: absolute;
  top: calc(100% + 4px);
  left: 0;
  min-width: 80px;
  overflow-y: auto;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 0;
  box-shadow: 0 2px 6px color-mix(in srgb, var(--text-primary) 15%, transparent);
  z-index: 1000;
  padding-block: 2px;
}
.ct-dropdown-item {
  display: block;
  width: 100%;
  padding: 0 12px;
  height: 36px;
  line-height: 36px;
  font-size: 13px;
  text-align: right;
  color: var(--text-primary);
  white-space: nowrap;
  border-radius: 0;
}
</style>
