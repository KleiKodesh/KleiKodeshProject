<script setup lang="ts">
import { ref, watch } from 'vue'
import {
  IconSearch20Regular,
  IconDismiss20Regular,
  IconFilter20Regular,
} from '@iconify-prerendered/vue-fluent'
import BottomSearchBar from '@/components/common/BottomSearchBar.vue'

const props = defineProps<{
  searchQuery: string
  isSearching: boolean
  filterCount: number
  disabled?: boolean
}>()
const emit = defineEmits<{
  search: [string]
  cancel: []
  toggleFilter: []
  clear: []
  'update:searchQuery': [string]
}>()

const inputRef = ref<HTMLInputElement | null>(null)
const localQuery = ref(props.searchQuery)

watch(
  () => props.searchQuery,
  (v) => {
    localQuery.value = v
  },
)
watch(localQuery, (v) => emit('update:searchQuery', v))

function handleSearch() {
  if (localQuery.value.trim()) emit('search', localQuery.value)
}
function handleClear() {
  localQuery.value = ''
  emit('clear')
  inputRef.value?.focus()
}

defineExpose({ focus: () => inputRef.value?.focus() })
</script>

<template>
  <BottomSearchBar>
    <template #left>
      <button
        class="bar-btn"
        :class="{ 'filter-active': filterCount > 0 }"
        :title="filterCount > 0 ? `סינון: ${filterCount} ספרים` : 'סינון תוצאות'"
        @click.stop="$emit('toggleFilter')"
      >
        <IconFilter20Regular />
      </button>
    </template>
    <input
      ref="inputRef"
      v-model="localQuery"
      type="text"
      class="search-input"
      placeholder="הזן טקסט לחיפוש..."
      :disabled="disabled"
      @keydown.enter="handleSearch"
      @keydown.esc="handleClear"
    />
    <template #right>
      <button
        class="bar-btn"
        :disabled="disabled || (!isSearching && !localQuery.trim())"
        :title="isSearching ? 'ביטול חיפוש' : 'חיפוש'"
        @click="isSearching ? $emit('cancel') : handleSearch()"
      >
        <div v-if="isSearching" class="spinner-wrap">
          <svg class="ring" viewBox="0 0 24 24">
            <circle
              cx="12"
              cy="12"
              r="10"
              fill="none"
              stroke-width="2"
              stroke="var(--border-color)"
            />
            <circle
              cx="12"
              cy="12"
              r="10"
              fill="none"
              stroke-width="2"
              stroke="var(--accent-color)"
              stroke-dasharray="31.4 31.4"
              stroke-linecap="round"
            />
          </svg>
          <IconDismiss20Regular class="cancel-icon" />
        </div>
        <IconSearch20Regular v-else />
      </button>
    </template>
  </BottomSearchBar>
</template>

<style scoped>
.search-input {
  flex: 1;
  background: none;
  border: none;
  outline: none;
  font-size: 13px;
  color: var(--text-primary);
  direction: rtl;
}
.search-input::placeholder {
  color: var(--text-secondary);
}
.bar-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  border-radius: 4px;
  flex-shrink: 0;
}
.bar-btn:disabled {
  opacity: 0.35;
  cursor: not-allowed;
}
.filter-active {
  color: var(--accent-color);
}
.spinner-wrap {
  position: relative;
  width: 20px;
  height: 20px;
  display: flex;
  align-items: center;
  justify-content: center;
}
.ring {
  width: 20px;
  height: 20px;
  animation: spin 1s linear infinite;
}
.cancel-icon {
  position: absolute;
  width: 12px;
  height: 12px;
  color: var(--text-secondary);
}
@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
