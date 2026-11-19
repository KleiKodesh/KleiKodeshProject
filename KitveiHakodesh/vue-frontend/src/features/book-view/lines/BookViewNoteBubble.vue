<script setup lang="ts">
/**
 * Editable note bubble, anchored to a note marker in the scroller.
 *
 * Positioning: the parent passes anchorRect (the marker's DOMRect relative to
 * the viewport). The bubble is Teleported to body and positioned with fixed
 * coordinates derived from anchorRect, flipped above the marker when too close
 * to the bottom. A small arrow points at the marker.
 *
 * Lifecycle:
 *   - Mounts and auto-focuses the text (cursor at the end).
 *   - The text area grows with its content (up to a cap, then scrolls).
 *   - Esc, Ctrl+Enter or the close button close the bubble.
 *   - Auto-saves (UPDATE_NOTE) on unmount — so closing the bubble for any
 *     reason (click-outside, scroll, navigation) persists the current text.
 *   - Delete button calls deleteNote() and emits 'deleted' so the parent can
 *     remove the note from its composable before unmounting.
 */
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { ref, computed, onMounted, onBeforeUnmount, nextTick } from 'vue'
import { useTextareaAutosize } from '@vueuse/core'
import { IconDelete24Regular, IconNote20Regular } from '@iconify-prerendered/vue-fluent'
import { HDate } from '@hebcal/hdate'
import { useDropdownClose } from '@/composables/useDropdownClose'
import type { Note } from './useBookViewNotes'

const props = defineProps<{
  note: Note
  anchorRect: DOMRect
  updateNote: (note: Note, text: string) => Promise<void>
  deleteNote: (note: Note) => Promise<void>
  /** Opened by hover: don't take focus until the user clicks in. */
  preview?: boolean
}>()

const emit = defineEmits<{
  close: []
  deleted: []
}>()

const MARGIN = 8
const ARROW = 7
const MAX_WIDTH = 380

const bubbleRef = ref<HTMLElement | null>(null)
const isDeleting = ref(false)
const { textarea: textareaRef, input: noteText } = useTextareaAutosize({ input: props.note.note })

// Resolved position — updated after mount with real bubble dimensions
const resolvedTop = ref<number | null>(null)
const resolvedLeft = ref<number | null>(null)
const resolvedWidth = ref<number | null>(null)
const placedAbove = ref(false)
const arrowX = ref(0)

const hostWindow = useHostWindow()

function computePosition() {
  const bubbleWidth = Math.min(MAX_WIDTH, hostWindow.innerWidth - MARGIN * 2)
  const anchorCenter = (props.anchorRect.left + props.anchorRect.right) / 2

  // Center on the marker, then keep inside the window
  let left = anchorCenter - bubbleWidth / 2
  left = Math.min(Math.max(MARGIN, left), hostWindow.innerWidth - bubbleWidth - MARGIN)

  const bubbleHeight = bubbleRef.value?.offsetHeight ?? 220
  let top = props.anchorRect.bottom + ARROW + 2
  placedAbove.value = false
  if (top + bubbleHeight > hostWindow.innerHeight - MARGIN) {
    const above = props.anchorRect.top - bubbleHeight - ARROW - 2
    if (above >= MARGIN) {
      top = above
      placedAbove.value = true
    }
  }
  top = Math.max(MARGIN, top)

  resolvedTop.value = top
  resolvedLeft.value = left
  resolvedWidth.value = bubbleWidth
  arrowX.value = Math.min(Math.max(14, anchorCenter - left), bubbleWidth - 14)
}

// Initial style — rendered off-screen so we can measure before showing
const style = computed(() => {
  const width = resolvedWidth.value ?? Math.min(MAX_WIDTH, hostWindow.innerWidth - MARGIN * 2)
  if (resolvedTop.value === null) {
    return {
      position: 'fixed' as const,
      top: '-9999px',
      left: '-9999px',
      width: `${width}px`,
      zIndex: '9998',
      visibility: 'hidden' as const,
    }
  }
  return {
    position: 'fixed' as const,
    top: `${resolvedTop.value}px`,
    left: `${resolvedLeft.value}px`,
    width: `${width}px`,
    zIndex: '9998',
    '--arrow-x': `${arrowX.value}px`,
  }
})

// Hebrew date in letters + time, e.g. "<day> <month> <year>, 14:05"
function formatTimestamp(ms: number): string {
  const d = new Date(ms)
  const time = d.toLocaleTimeString('he-IL', { hour: '2-digit', minute: '2-digit' })
  return `${new HDate(d).renderGematriya(true)}, ${time}`
}

const isEdited = computed(() => props.note.updatedAt !== props.note.createdAt)

useDropdownClose(bubbleRef, () => {
  if (!isDeleting.value) emit('close')
})

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape' || (e.key === 'Enter' && (e.ctrlKey || e.metaKey))) {
    e.preventDefault()
    e.stopPropagation()
    emit('close')
  }
}

onMounted(() => {
  nextTick(async () => {
    computePosition()
    // A visibility:hidden element cannot take focus: wait for Vue to render the
    // bubble at its resolved (visible) position, then focus.
    await nextTick()
    const ta = textareaRef.value
    if (ta && !props.preview) {
      ta.focus()
      ta.setSelectionRange(ta.value.length, ta.value.length)
    }
  })
})

onBeforeUnmount(async () => {
  if (!isDeleting.value && (noteText.value ?? '') !== props.note.note) {
    await props.updateNote(props.note, noteText.value ?? '')
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
  <Teleport :to="hostWindow.document.body">
    <div
      dir="rtl"
      ref="bubbleRef"
      class="note-bubble"
      :class="{ above: placedAbove }"
      :style="style"
      @click.stop
      @keydown="onKeydown"
    >
      <div class="note-bubble-header">
        <IconNote20Regular class="note-icon" />
        <span
          class="note-bubble-time"
          :title="isEdited ? $t('bookView.note.updated', { time: formatTimestamp(note.updatedAt) }) : undefined"
        >
          {{ formatTimestamp(isEdited ? note.updatedAt : note.createdAt) }}
        </span>
        <button class="icon-button delete-button" :title="$t('bookView.note.delete')" :aria-label="$t('bookView.note.delete')" @click="onDelete">
          <IconDelete24Regular />
        </button>
      </div>
      <div v-if="note.quote" class="note-bubble-quote" dir="rtl" :title="note.quote">{{ note.quote }}</div>
      <textarea
        ref="textareaRef"
        v-model="noteText"
        class="note-textarea"
        dir="rtl"
        rows="3"
        :placeholder="$t('bookView.note.placeholder')"
      />
    </div>
  </Teleport>
</template>

<style scoped>
.note-bubble {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.12);
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 8px 10px 6px;
  direction: rtl;
  text-align: right;
}

.note-bubble-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding-bottom: 6px;
  border-bottom: 1px solid var(--border-color);
}

.note-icon {
  width: 16px;
  height: 16px;
  color: var(--accent-color);
  flex-shrink: 0;
}

.note-bubble-quote {
  font-family: var(--text-font);
  font-size: 13px;
  color: var(--text-secondary);
  background: color-mix(in srgb, var(--accent-color) 6%, transparent);
  border-inline-start: 2px solid var(--accent-color);
  border-radius: 3px;
  padding: 3px 8px;
  line-height: 1.4;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.note-textarea {
  width: 100%;
  box-sizing: border-box;
  resize: none;
  border: none;
  background: transparent;
  color: var(--text-primary);
  font-family: var(--text-font);
  font-size: 14px;
  line-height: 1.6;
  padding: 0;
  outline: none;
  direction: rtl;
  text-align: right;
  min-height: calc(1.6em * 3);
  max-height: 40vh;
  overflow-y: auto;
}

.note-textarea::placeholder {
  color: var(--text-secondary);
  opacity: 0.6;
}

.note-bubble-time {
  flex: 1;
  font-size: 12px;
  color: var(--text-secondary);
}

.icon-button {
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
  padding: 0;
}

.delete-button:hover {
  color: var(--status-danger);
  background: color-mix(in srgb, var(--status-danger) 10%, transparent);
}
</style>