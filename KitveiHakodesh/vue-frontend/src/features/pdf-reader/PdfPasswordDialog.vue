<script setup lang="ts">
import { nextTick, onMounted, ref, watch } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'

/**
 * Asks for a protected PDF's password (PDF.js's password_prompt.js): OK hands it to the loading
 * task, Cancel stops the open. `incorrect`: the last password was wrong - ask again.
 */
const props = defineProps<{ incorrect: boolean }>()
const emit = defineEmits<{ submit: [password: string]; cancel: [] }>()
const hostWindow = useHostWindow()
const password = ref('')
const input = ref<HTMLInputElement | null>(null)

function focusInput() {
  void nextTick(() => {
    input.value?.focus()
    input.value?.select()
  })
}
onMounted(focusInput)
watch(() => props.incorrect, focusInput)

function submit() {
  if (password.value) emit('submit', password.value)
}

useEventListener(hostWindow, 'keydown', (e: KeyboardEvent) => {
  if (e.code === 'Escape') {
    e.preventDefault()
    e.stopPropagation()
    emit('cancel')
  }
}, { capture: true })
</script>

<template>
  <Teleport :to="hostWindow.document.body">
    <div class="pw-backdrop">
      <form class="pw-dialog" role="dialog" aria-modal="true" :aria-label="$t('pdfReader.password.title')" @submit.prevent="submit">
        <h3 class="pw-title">{{ $t('pdfReader.password.title') }}</h3>
        <p class="pw-text" :class="{ error: incorrect }">
          {{ incorrect ? $t('pdfReader.password.incorrect') : $t('pdfReader.password.prompt') }}
        </p>
        <input
          ref="input"
          v-model="password"
          class="pw-input"
          type="password"
          autocomplete="off"
          :aria-label="$t('pdfReader.password.label')"
          :placeholder="$t('pdfReader.password.label')"
        />
        <div class="pw-actions">
          <button type="submit" class="pw-btn primary" :disabled="!password">{{ $t('pdfReader.password.ok') }}</button>
          <button type="button" class="pw-btn" @click="emit('cancel')">{{ $t('pdfReader.cancel') }}</button>
        </div>
      </form>
    </div>
  </Teleport>
</template>

<style scoped>
/* The app's dialog look, as PdfPrintChoiceDialog. */
.pw-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 10000;
}
.pw-dialog {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 20px 20px 14px;
  width: 380px;
  max-width: calc(100vw - 32px);
  max-height: 80vh;
  overflow: auto;
  scrollbar-width: thin;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.pw-title {
  margin: 0;
  font-size: 14px;
  color: var(--text-primary);
}
.pw-text {
  margin: 0;
  font-size: 12px;
  color: var(--text-primary);
}
.pw-text.error {
  color: var(--status-danger);
}
.pw-input {
  height: 30px;
  padding: 0 8px;
  font-size: 13px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: var(--control-bg);
  color: var(--text-primary);
}
.pw-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding-top: 10px;
  border-top: 1px solid var(--border-color);
}
.pw-btn {
  height: 30px;
  padding: 0 14px;
  font-size: 12px;
  border: 1px solid var(--border-color);
  background: var(--control-bg);
  color: var(--text-primary);
  border-radius: 4px;
}
.pw-btn:hover:not(:disabled) {
  background: var(--control-bg-hover);
}
.pw-btn:disabled {
  opacity: 0.5;
}
.pw-btn.primary {
  font-weight: 600;
}
</style>
