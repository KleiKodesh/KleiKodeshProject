<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useDebounceFn } from '@vueuse/core'
import { IconChevronDown20Regular, IconChevronUp20Regular, IconDismiss20Regular } from '@iconify-prerendered/vue-fluent'

/**
 * The FastDocView in-document search bar: the query (type="search": its own clear X, Esc closes),
 * the match position "n / total" (the total grows while the search runs), previous / next.
 * Enter = next, Shift+Enter = previous.
 */
const props = defineProps<{ initialQuery: string; currentHit: number; hitCount: number; isSearching: boolean }>()
const emit = defineEmits<{ query: [text: string]; next: []; prev: []; close: [] }>()

const input = ref<HTMLInputElement | null>(null)
const text = ref(props.initialQuery)
onMounted(() => {
  input.value?.focus()
  input.value?.select()
})
const emitQuery = useDebounceFn((value: string) => emit('query', value), 250)

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('close')
  } else if (event.key === 'Enter') {
    event.preventDefault()
    if (event.shiftKey) emit('prev')
    else emit('next')
  }
}
defineExpose({ focus: () => input.value?.focus() })
</script>

<template>
  <div class="fast-doc-view-search-bar">
    <input
      ref="input"
      v-model="text"
      type="search"
      class="fast-doc-view-search-input"
      :placeholder="$t('components.searchPlaceholder')"
      @input="emitQuery(text)"
      @keydown="onKeydown"
    />
    <span class="fast-doc-view-search-count" :class="{ 'is-searching': isSearching }">
      {{ hitCount > 0 ? `${currentHit + 1} / ${hitCount}` : text.trim() && !isSearching ? '0' : '' }}{{ isSearching ? '...' : '' }}
    </span>
    <button :title="$t('fastDocView.searchPrevious')" :disabled="hitCount === 0" @click="emit('prev')">
      <IconChevronUp20Regular />
    </button>
    <button :title="$t('fastDocView.searchNext')" :disabled="hitCount === 0" @click="emit('next')">
      <IconChevronDown20Regular />
    </button>
    <button :title="$t('fastDocView.searchClose')" @click="emit('close')">
      <IconDismiss20Regular />
    </button>
  </div>
</template>

<style scoped>
.fast-doc-view-search-bar {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 4px 8px;
  background: var(--bg-toolbar);
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
}
.fast-doc-view-search-input {
  flex: 1;
  min-width: 0;
  padding: 4px 8px;
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  color: var(--text-primary);
  font: inherit;
}
.fast-doc-view-search-count {
  min-width: 56px;
  text-align: center;
  font-size: 0.85em;
  color: var(--text-secondary);
}
button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--toolbar-button-size);
  height: var(--toolbar-button-size);
  border-radius: 4px;
}
button svg {
  width: 16px;
  height: 16px;
}
button:disabled {
  opacity: 0.4;
}
</style>
