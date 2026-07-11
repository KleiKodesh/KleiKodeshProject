<script setup lang="ts">
import { ref, nextTick } from 'vue'
import { IconFolderOpen20Regular, IconDismiss20Regular } from '@iconify-prerendered/vue-fluent'

const props = defineProps<{
  value: string
  placeholder: string
  /** Show a clear (×) button when a value is set. */
  clearable?: boolean
  /** Disable the folder-picker button. */
  disabled?: boolean
  /** Allow clicking the path text to enter inline edit mode. */
  editable?: boolean
}>()

const emit = defineEmits<{
  (event: 'pick'): void
  (event: 'clear'): void
  (event: 'commit', value: string): void
}>()

const editing = ref(false)
const editValue = ref('')
const inputRef = ref<HTMLInputElement | null>(null)

function startEditing() {
  if (!props.editable) return
  editValue.value = props.value
  editing.value = true
  nextTick(() => inputRef.value?.focus())
}

function commitEdit() {
  editing.value = false
  emit('commit', editValue.value)
}

function cancelEdit() {
  editing.value = false
}
</script>

<template>
  <div class="path-field" :class="{ editing }">
    <button
      class="folder-btn"
      :disabled="props.disabled"
      title="בחר"
      @click="emit('pick')"
    >
      <IconFolderOpen20Regular />
    </button>

    <input
      v-if="editing"
      ref="inputRef"
      v-model="editValue"
      class="path-input"
      dir="ltr"
      @blur="commitEdit"
      @keydown.enter="commitEdit"
      @keydown.escape="cancelEdit"
    />
    <span
      v-else
      class="path-text"
      :class="{ placeholder: !props.value, clickable: props.editable }"
      dir="ltr"
      @click="startEditing"
    >
      {{ props.value || props.placeholder }}
    </span>

    <button
      v-if="!editing && props.clearable && props.value"
      class="clear-btn"
      title="נקה נתיב"
      @click="emit('clear')"
    >
      <IconDismiss20Regular />
    </button>
  </div>
</template>

<style scoped>
.path-field {
  display: flex;
  align-items: center;
  width: 100%;
  height: 28px;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-secondary);
  overflow: hidden;
  transition: border-color 0.1s;
}

.path-field:hover {
  border-color: color-mix(in srgb, var(--text-secondary) 50%, transparent);
}

.path-field.editing {
  border-color: var(--accent-color);
}

.folder-btn {
  flex-shrink: 0;
  width: 28px;
  height: 28px;
  padding: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  border: none;
  border-inline-end: 1px solid var(--border-color);
  border-radius: 0;
  background: transparent;
  color: var(--text-secondary);
}

.folder-btn:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}

.folder-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.path-text {
  flex: 1;
  padding: 0 8px;
  font-size: 11px;
  color: var(--text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  text-align: left;
  min-width: 0;
}

.path-text.placeholder {
  direction: rtl;
  text-align: right;
  opacity: 0.6;
}

.path-text.clickable {
  cursor: text;
}

.path-text.clickable:hover {
  color: var(--text-primary);
}

.path-input {
  flex: 1;
  height: 100%;
  padding: 0 8px;
  font-size: 11px;
  direction: ltr;
  text-align: left;
  background: transparent;
  border: none;
  outline: none;
  color: var(--text-primary);
  min-width: 0;
}

.clear-btn {
  flex-shrink: 0;
  width: 28px;
  height: 28px;
  padding: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  border: none;
  border-inline-start: 1px solid var(--border-color);
  border-radius: 0;
  background: transparent;
  color: var(--text-secondary);
}

.clear-btn:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
</style>
