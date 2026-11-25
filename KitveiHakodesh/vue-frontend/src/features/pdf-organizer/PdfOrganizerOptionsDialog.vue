<script setup lang="ts">
/**
 * The organizer's option dialogs, a real modal in the organizer's window:
 * export as images (format, resolution, which pages) and split (every N pages / at the selection).
 */
import { ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import type { ImageFormat } from './pageImages'

export type ImagesOptions = { format: ImageFormat; dpi: number; selectedOnly: boolean }
export type SplitOptions = { mode: 'every' | 'selection'; every: number }

const props = defineProps<{ kind: 'images' | 'split'; hasSelection: boolean }>()
const emit = defineEmits<{ cancel: []; images: [ImagesOptions]; split: [SplitOptions] }>()
const hostWindow = useHostWindow()

const DPI_CHOICES = [72, 96, 150, 200, 300, 600]
const format = ref<ImageFormat>('png')
const dpi = ref(150)
const selectedOnly = ref(props.hasSelection)
const splitMode = ref<'every' | 'selection'>(props.hasSelection ? 'selection' : 'every')
const every = ref(1)

function confirm() {
  if (props.kind === 'images') emit('images', { format: format.value, dpi: dpi.value, selectedOnly: selectedOnly.value })
  else emit('split', { mode: splitMode.value, every: Math.max(1, Math.floor(every.value || 1)) })
}

useEventListener(hostWindow, 'keydown', (e: KeyboardEvent) => {
  if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); emit('cancel') }
  else if (e.key === 'Enter') { e.preventDefault(); e.stopPropagation(); confirm() }
}, { capture: true })
</script>

<template>
  <Teleport :to="hostWindow.document.body">
    <div class="opt-backdrop" @click.self="emit('cancel')">
      <div class="opt-dialog toc-thin-scroll" role="dialog" aria-modal="true">
        <template v-if="kind === 'images'">
          <p class="opt-title">{{ $t('pdfOrganizer.images.title') }}</p>
          <label class="opt-row">
            <span>{{ $t('pdfOrganizer.images.format') }}</span>
            <select v-model="format">
              <option value="png">PNG</option>
              <option value="jpeg">JPEG</option>
            </select>
          </label>
          <label class="opt-row">
            <span>{{ $t('pdfOrganizer.images.resolution') }}</span>
            <select v-model.number="dpi">
              <option v-for="d in DPI_CHOICES" :key="d" :value="d">{{ d }} DPI</option>
            </select>
          </label>
          <label class="opt-row">
            <span>{{ $t('pdfOrganizer.scope') }}</span>
            <select v-model="selectedOnly">
              <option :value="true" :disabled="!hasSelection">{{ $t('pdfOrganizer.scopeSelected') }}</option>
              <option :value="false">{{ $t('pdfOrganizer.scopeAll') }}</option>
            </select>
          </label>
        </template>
        <template v-else>
          <p class="opt-title">{{ $t('pdfOrganizer.split.title') }}</p>
          <label class="opt-check">
            <input v-model="splitMode" type="radio" value="every" />
            <span>{{ $t('pdfOrganizer.split.every') }}</span>
            <input v-model.number="every" class="opt-number" type="number" min="1" :disabled="splitMode !== 'every'" />
          </label>
          <label class="opt-check">
            <input v-model="splitMode" type="radio" value="selection" :disabled="!hasSelection" />
            <span>{{ $t('pdfOrganizer.split.atSelection') }}</span>
          </label>
          <p class="opt-hint">{{ $t('pdfOrganizer.split.hint') }}</p>
        </template>
        <div class="opt-actions">
          <button class="opt-ok" @click="confirm">{{ $t('pdfOrganizer.chooseFolder') }}</button>
          <button @click="emit('cancel')">{{ $t('components.cancel') }}</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.opt-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
}
.opt-dialog {
  background: var(--bg-secondary);
  color: var(--text-primary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 20px 20px 14px;
  width: 340px;
  max-height: 90vh;
  overflow: auto;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.opt-title {
  margin: 0;
  font-weight: 600;
}
.opt-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.opt-row select,
.opt-number {
  min-width: 120px;
  height: 28px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: var(--bg-primary);
  color: inherit;
}
.opt-number {
  min-width: 0;
  width: 64px;
}
.opt-check {
  display: flex;
  align-items: center;
  gap: 8px;
}
.opt-hint {
  margin: 0;
  font-size: 12px;
  color: var(--text-secondary);
}
.opt-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 4px;
}
.opt-actions button {
  padding: 6px 14px;
  border-radius: 4px;
  border: 1px solid var(--border-color);
}
.opt-actions .opt-ok {
  background: var(--accent-color);
  border-color: var(--accent-color);
  color: #fff;
}
</style>
