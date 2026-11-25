<script setup lang="ts">
import { useEventListener } from '@vueuse/core'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'

/**
 * Asked before printing when the document has edits not saved in the file: save them and print
 * the file (when it can be saved), or print the file as it is on disk. Only the file is ever printed.
 */
defineProps<{ canSave?: boolean }>()
const emit = defineEmits<{ choose: [choice: 'saveAndPrint' | 'withoutChanges' | 'cancel'] }>()
const hostWindow = useHostWindow()

useEventListener(hostWindow, 'keydown', (e: KeyboardEvent) => {
  if (e.code === 'Escape') {
    e.preventDefault()
    emit('choose', 'cancel')
  }
})
</script>

<template>
  <Teleport :to="hostWindow.document.body">
    <div class="print-backdrop" @click.self="emit('choose', 'cancel')">
      <div class="print-dialog" role="dialog" aria-modal="true" :aria-label="$t('pdfReader.printChangesTitle')">
        <h3 class="print-title">{{ $t('pdfReader.printChangesTitle') }}</h3>
        <p class="print-text">{{ $t('pdfReader.printChangesText') }}</p>
        <div class="print-actions">
          <button v-if="canSave" class="print-btn primary" autofocus @click="emit('choose', 'saveAndPrint')">{{ $t('pdfReader.saveAndPrint') }}</button>
          <button class="print-btn" :class="{ primary: !canSave }" :autofocus="!canSave" @click="emit('choose', 'withoutChanges')">{{ $t('pdfReader.printWithoutChanges') }}</button>
          <button class="print-btn" @click="emit('choose', 'cancel')">{{ $t('pdfReader.cancel') }}</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
/* The app's dialog look (components/AlertDialog.vue), as PdfPropertiesDialog. */
.print-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 10000;
}
.print-dialog {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 20px 20px 14px;
  width: 420px;
  max-width: calc(100vw - 32px);
  max-height: 80vh;
  overflow: auto;
  scrollbar-width: thin;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.print-title {
  margin: 0;
  font-size: 14px;
  color: var(--text-primary);
}
.print-text {
  margin: 0;
  font-size: 12px;
  color: var(--text-primary);
}
.print-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
  padding-top: 10px;
  border-top: 1px solid var(--border-color);
}
.print-btn {
  height: 30px;
  padding: 0 14px;
  font-size: 12px;
  border: 1px solid var(--border-color);
  background: var(--control-bg);
  color: var(--text-primary);
  border-radius: 4px;
}
.print-btn:hover {
  background: var(--control-bg-hover);
}
.print-btn.primary {
  font-weight: 600;
}
</style>
