<script setup lang="ts">
/**
 * Editable note bubble, anchored to a note marker in the scroller.
 *
 * Positioning: the parent passes anchorRect (the marker's DOMRect relative to
 * the viewport). The bubble is Teleported to body and positioned with fixed
 * coordinates derived from anchorRect, flipped if too close to a viewport edge.
 *
 * Lifecycle:
 *   - Mounts with empty textarea and auto-focuses it.
 *   - Auto-saves (UPDATE_NOTE) on unmount — so closing the bubble for any
 *     reason (click-outside, scroll, navigation) persists the current text.
 *   - Delete button calls deleteNote() and emits 'deleted' so the parent can
 *     remove the note from its composable before unmounting.
 */
import { ref, computed, onMounted, onBeforeUnmount, nextTick } from 'vue'
import { IconDelete24Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import type { Note } from './useBookViewNotes'

const props = defineProps<{
  note: Note
  anchorRect: DOMRect
  updateNote: (note: Note, text: string) => Promise<void>
  deleteNote: (note: Note) => Promise<void>
}>()

const emit = defineEmits<{
  close: []
  deleted: []
}>()

const bubbleRef = ref<HTMLElement | null>(null)
const textareaRef = ref<HTMLTextAreaElement | null>(null)
const noteText = ref(props.note.note)
const isDeleting = ref(false)

// Position the bubble below (or above) the anchor marker
const style = computed(() => {
  const MARGIN = 8
  const MAX_WIDTH = 360
  const BUBBLE_HEIGHT = 280 // approximate for vertical flip check

  // Available width between left edge and right viewport edge
  const availableWidth = window.innerWidth - MARGIN * 2
  const bubbleWidth = Math.min(MAX_WIDTH, availableWidth)

  let left = props.anchorRect.left
  let top = props.anchorRect.bottom + MARGIN

  // Clamp left so bubble doesn't overflow the right viewport edge
  if (left + bubbleWidth > window.innerWidth - MARGIN) {
    left = Math.max(MARGIN, window.innerWidth - bubbleWidth - MARGIN)
  }

  // Flip vertically if too close to bottom
  if (top + BUBBLE_HEIGHT > window.innerHeight - MARGIN) {
    top = props.anchorRect.top - BUBBLE_HEIGHT - MARGIN
  }

  return {
    position: 'fixed' as const,
    left: `${left}px`,
    top: `${top}px`,
    width: `${bubbleWidth}px`,
    zIndex: '9998',
  }
})

// Format timestamp as a locale date-time string
function formatTimestamp(ms: number): string {
  return new Date(ms).toLocaleString('he-IL', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

useDropdownClose(bubbleRef, () => {
  if (!isDeleting.value) emit('close')
})

onMounted(() => {
  nextTick(() => textareaRef.value?.focus())
})

onBeforeUnmount(async () => {
  if (!isDeleting.value) {
    await props.updateNote(props.note, noteText.value)
  }
})

async function onDelete() {
  isDeleting.value = true
  await props.deleteNote(props.note)
  emit('deleted')
  emit('close')
}
</script>

<template>
  <Teleport to="body">
    <div ref="bubbleRef" class="note-bubble" :style="style" @click.stop>
      <div class="note-bubble-header">
        <span class="note-bubble-quote" :title="note.quote">{{ note.quote }}</span>
        <button class="delete-button" :aria-label="'מחק הערה'" @click="onDelete">
          <IconDelete24Regular />
        </button>
      </div>
      <textarea
        ref="textareaRef"
        v-model="noteText"
        class="note-textarea"
        dir="auto"
        placeholder="הזן הערה..."
        rows="7"
      />
      <div class="note-bubble-timestamps">
        <span>{{ formatTimestamp(note.createdAt) }}</span>
        <span v-if="note.updatedAt !== note.createdAt">עודכן {{ formatTimestamp(note.updatedAt) }}</span>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.note-bubble {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow:
    0 4px 12px rgba(0, 0, 0, 0.15),
    0 16px 40px rgba(0, 0, 0, 0.1);
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 10px;
  direction: rtl;
}

.note-bubble-header {
  display: flex;
  align-items: flex-start;
  gap: 6px;
}

.note-bubble-quote {
  flex: 1;
  font-size: 11px;
  color: var(--text-secondary);
  font-style: italic;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  border-inline-end: 2px solid var(--accent-color);
  padding-inline-end: 6px;
  line-height: 1.4;
}

.delete-button {
  width: 24px;
  height: 24px;
  border-radius: 4px;
  border: none;
  background: none;
  color: var(--text-secondary);
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  padding: 0;
}

.delete-button:hover {
  color: #e53935;
  background: color-mix(in srgb, #e53935 10%, transparent);
}

.delete-button:active {
  transform: scale(0.92);
}

.note-textarea {
  width: 100%;
  box-sizing: border-box;
  resize: vertical;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: var(--input-bg, var(--bg-primary));
  color: var(--text-primary);
  font-family: var(--text-font);
  font-size: 13px;
  line-height: 1.6;
  padding: 6px 8px;
  outline: none;
  min-height: 140px;
}

.note-textarea:focus {
  border-color: var(--accent-color);
}

.note-bubble-timestamps {
  display: flex;
  flex-direction: column;
  gap: 1px;
  font-size: 10px;
  color: var(--text-secondary);
  opacity: 0.7;
}
</style>
