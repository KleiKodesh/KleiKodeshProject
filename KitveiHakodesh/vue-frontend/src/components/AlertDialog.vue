<script setup lang="ts">
import { useEventListener } from '@vueuse/core'

defineProps<{
  message: string
}>()

const emit = defineEmits<{ close: [] }>()

useEventListener('keydown', (e: KeyboardEvent) => {
  if (e.code === 'Enter' || e.code === 'Escape') {
    e.preventDefault()
    emit('close')
  }
})
</script>

<template>
  <Teleport to="body">
    <div class="alert-backdrop" @click.self="emit('close')">
      <div class="alert-dialog" role="alertdialog" aria-modal="true">
        <p class="alert-message">{{ message }}</p>
        <div class="alert-actions">
          <button class="alert-ok-btn" autofocus @click="emit('close')">אישור</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.alert-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 10000;
}

.alert-dialog {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 20px 20px 14px;
  width: 280px;
  direction: rtl;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.alert-message {
  margin: 0;
  font-size: 13px;
  color: var(--text-primary);
  line-height: 1.6;
}

.alert-actions {
  display: flex;
  justify-content: flex-end;
  padding-top: 10px;
  border-top: 1px solid var(--border-color);
}

.alert-ok-btn {
  height: 30px;
  padding: 0 20px;
  font-size: 12px;
  border: 1px solid var(--border-color);
  background: var(--bg-toolbar);
  color: var(--text-primary);
  border-radius: 4px;
}

.alert-ok-btn:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, var(--bg-toolbar));
}
</style>
