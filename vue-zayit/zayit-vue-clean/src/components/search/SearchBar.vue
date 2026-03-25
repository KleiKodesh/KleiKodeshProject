<script setup lang="ts">
import { ref, watch } from 'vue'
import { IconSearch24Regular, IconDismiss24Regular, IconFilter24Regular } from '@iconify-prerendered/vue-fluent'

const props = defineProps<{
  searchQuery: string
  isSearching: boolean
  filterCount: number
}>()

const emit = defineEmits<{
  search: [query: string]
  cancel: []
  toggleFilter: []
  clear: []
  'update:searchQuery': [query: string]
}>()

const inputRef   = ref<HTMLInputElement | null>(null)
const localQuery = ref(props.searchQuery)

watch(() => props.searchQuery, v => { localQuery.value = v })
watch(localQuery, v => emit('update:searchQuery', v))

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
  <div class="search-bar">
    <div class="search-inner">
      <input
        ref="inputRef"
        v-model="localQuery"
        type="text"
        class="search-input"
        placeholder="חיפוש בכל הספרים..."
        @keydown.enter="handleSearch"
        @keydown.esc="handleClear"
      />
      <button
        class="bar-btn"
        :class="{ 'filter-active': filterCount > 0 }"
        :title="filterCount > 0 ? `סינון: ${filterCount} ספרים` : 'סינון תוצאות'"
        @click.stop="$emit('toggleFilter')"
      >
        <IconFilter24Regular />
      </button>
      <button
        class="bar-btn search-btn"
        :class="{ searching: isSearching }"
        :disabled="!isSearching && !localQuery.trim()"
        :title="isSearching ? 'ביטול חיפוש' : 'חיפוש'"
        @click="isSearching ? $emit('cancel') : handleSearch()"
      >
        <div v-if="isSearching" class="spinner-wrap">
          <svg class="ring" viewBox="0 0 24 24">
            <circle class="ring-bg" cx="12" cy="12" r="10" fill="none" stroke-width="2" />
            <circle class="ring-spin" cx="12" cy="12" r="10" fill="none" stroke-width="2"
              stroke-dasharray="31.4 31.4" stroke-linecap="round" />
          </svg>
          <IconDismiss24Regular class="cancel-icon" />
        </div>
        <IconSearch24Regular v-else />
      </button>
    </div>
  </div>
</template>

<style scoped>
.search-bar {
  padding: 5px 10px 6px;
  background: var(--bg-secondary);
  border-top: 1px solid var(--border-color);
}

.search-inner {
  display: flex;
  align-items: center;
  gap: 2px;
  background: color-mix(in srgb, var(--text-secondary) 12%, transparent);
  border-radius: 6px;
  padding: 0 4px;
}

.search-input {
  flex: 1;
  background: none;
  border: none;
  outline: none;
  font-size: 14px;
  color: var(--text-primary);
  padding: 8px 6px;
  direction: rtl;
}

.search-input::placeholder { color: var(--text-secondary); }

.bar-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  padding: 6px;
  border-radius: 4px;
  flex-shrink: 0;
}

.bar-btn:disabled { opacity: 0.35; cursor: not-allowed; }
.filter-active { color: var(--accent-color); }

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

.ring-bg   { stroke: var(--border-color); }
.ring-spin { stroke: var(--accent-color); transform-origin: center; }

.cancel-icon {
  position: absolute;
  width: 12px;
  height: 12px;
  color: var(--text-secondary);
}

@keyframes spin { to { transform: rotate(360deg); } }
</style>
