<template>
  <button 
    class="toggle-btn" 
    :class="{ 
      active: modelValue,
      'btn-small': size === 'small',
      'btn-medium': size === 'medium',
      'btn-large': size === 'large',
      'input-container-btn': variant === 'input-container'
    }"
    @click="$emit('update:modelValue', !modelValue)"
    :title="title"
    :disabled="disabled"
  >
    <slot>
      {{ label }}
    </slot>
  </button>
</template>

<script setup lang="ts">
interface Props {
  modelValue: boolean
  label?: string
  title?: string
  disabled?: boolean
  size?: 'small' | 'medium' | 'large'
  variant?: 'default' | 'input-container'
}

withDefaults(defineProps<Props>(), {
  label: '',
  title: '',
  disabled: false,
  size: 'medium',
  variant: 'default'
})

defineEmits<{
  'update:modelValue': [value: boolean]
}>()
</script>

<style scoped>
.toggle-btn {
  border: 1px solid #ccc;
  background: white;
  cursor: pointer;
  font-family: inherit;
  border-radius: 2px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: background-color 0.2s ease;
}

.toggle-btn:hover:not(:disabled) {
  background: #e8e8e8;
}

.toggle-btn.active {
  background: #cce8ff;
  border-color: #007acc;
}

.toggle-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

/* Size variants */
.btn-small {
  width: 24px;
  height: 24px;
  font-size: 12px;
  padding: 4px;
}

.btn-medium {
  width: 32px;
  height: 32px;
  font-size: 12px;
  padding: 6px;
}

.btn-large {
  height: 28px;
  font-size: 12px;
  padding: 5px 12px;
  width: auto;
  min-width: fit-content;
}

/* Input container variant - override size classes */
.input-container-btn.btn-small,
.input-container-btn.btn-medium,
.input-container-btn.btn-large,
.input-container-btn {
  border: none;
  background: transparent;
  border-radius: 2px;
  width: 32px;
  height: 100%;
  padding: 0;
  min-height: 32px;
}

.input-container-btn:hover:not(:disabled) {
  background: #e8e8e8;
}

.input-container-btn.active {
  background: #cce8ff;
  border: none;
}
</style>