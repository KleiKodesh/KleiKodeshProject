<script setup lang="ts">
import { useEventListener } from '@vueuse/core'

defineProps<{
  title: string
  desc?: string
}>()

const emit = defineEmits<{
  confirm: []
  cancel: []
}>()

useEventListener('keydown', (e: KeyboardEvent) => {
  if (e.key === 'Enter') {
    e.preventDefault()
    emit('confirm')
  }
  if (e.key === 'Escape') {
    e.preventDefault()
    emit('cancel')
  }
})
</script>

<template>
  <Teleport to="body">
    <div class="confirm-backdrop" @click.self="emit('cancel')">
      <div class="confirm-dialog">
        <p class="confirm-title">{{ title }}</p>
        <p v-if="desc" class="confirm-desc">{{ desc }}</p>
        <div class="confirm-actions">
          <button class="confirm-ok-btn" @click="emit('confirm')">אישור</button>
          <button class="confirm-cancel-btn" @click="emit('cancel')">ביטול</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.confirm-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
}

.confirm-dialog {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 20px 20px 14px;
  width: 320px;
  direction: rtl;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.confirm-title {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
}

.confirm-desc {
  margin: 0;
  font-size: 12px;
  color: var(--text-secondary);
  line-height: 1.6;
  text-align: justify;
}

.confirm-actions {
  display: flex;
  flex-direction: row-reverse;
  justify-content: flex-start;
  gap: 8px;
  padding-top: 10px;
  margin-top: 2px;
  border-top: 1px solid var(--border-color);
}

.confirm-cancel-btn {
  height: 30px;
  padding: 0 14px;
  font-size: 12px;
  border: 1px solid var(--border-color);
  background: var(--bg-toolbar);
  color: var(--text-primary);
  border-radius: 4px;
}
.confirm-cancel-btn:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, var(--bg-toolbar));
}

.confirm-ok-btn {
  height: 30px;
  padding: 0 14px;
  font-size: 12px;
  color: #e53e3e;
  border: 1px solid color-mix(in srgb, #e53e3e 40%, transparent);
  background: color-mix(in srgb, #e53e3e 8%, transparent);
  border-radius: 4px;
}
.confirm-ok-btn:hover {
  background: color-mix(in srgb, #e53e3e 16%, transparent);
}
</style>
