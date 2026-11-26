<script setup lang="ts">
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
/**
 * The lines context menu's "add note" row. A component rather than a plain text item
 * only because a note needs a selection, and with none it shows the "select text first"
 * alert instead of acting - which a text item has nowhere to render.
 */
import { ref } from 'vue'
import { IconNoteAdd20Regular } from '@iconify-prerendered/vue-fluent'
import AlertDialog from '@/components/AlertDialog.vue'

const hostWindow = useHostWindow()

const props = defineProps<{
  onAddNote: () => void
}>()

const emit = defineEmits<{ close: [] }>()

const showNoSelectionAlert = ref(false)

function hasSelection(): boolean {
  const sel = hostWindow.getSelection()
  return !!sel && !sel.isCollapsed && (sel.toString().trim().length > 0)
}

function onNote() {
  if (!hasSelection()) { showNoSelectionAlert.value = true; return }
  props.onAddNote()
  emit('close')
}
</script>

<template>
  <div class="note-row" @click="onNote">
    <AlertDialog
      v-if="showNoSelectionAlert"
      :message="$t('bookView.annotation.selectFirst')"
      @close="showNoSelectionAlert = false"
    />
    <IconNoteAdd20Regular class="row-icon" />
    <span class="note-label">{{ $t('bookView.annotation.addNote') }}</span>
  </div>
</template>

<style scoped>
/* Takes ContextMenu.vue's published row metrics, like BookViewAnnotationMenuRow - see the
   note there on why these are variables and not literals. */
.note-row {
  display: flex;
  align-items: center;
  padding: var(--context-menu-row-padding-block) var(--context-menu-row-padding-inline);
  box-sizing: border-box;
  height: var(--context-menu-row-height);
  cursor: pointer;
  font-size: 12px;
  /* ContextMenu's icon-to-label gap */
  gap: 6px;
}

.row-icon {
  flex-shrink: 0;
  width: 16px;
  height: 16px;
}

.note-row:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

.note-row:active {
  background: color-mix(in srgb, var(--text-primary) 13%, transparent);
}

.note-label {
  color: var(--text-primary);
}
</style>
