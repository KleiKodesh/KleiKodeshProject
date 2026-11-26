<script setup lang="ts">
import { IconDelete16Regular, IconEdit16Regular } from '@iconify-prerendered/vue-fluent'
import { computed, nextTick, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useEventListener } from '@vueuse/core'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import type { PdfNotes } from './usePdfNotes'

/**
 * A note's card (hover: its text; pinned by a click: with Edit / Delete) and the note box
 * (write / edit a note: Save = Ctrl+Enter, Cancel = Esc), beside the note's bubble on the page.
 * Drawn in the host window, fixed, re-placed as the pages scroll or zoom (notes.layoutTick).
 */
const props = defineProps<{ notes: PdfNotes }>()
const n = props.notes
const hostWindow = useHostWindow()
const { locale } = useI18n()
const dateFormat = computed(() => new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'short' }))

const GAP = 4
const MARGIN = 8
/** Where a panel goes: below the bubble (above when there is no room), inside the window. */
function placeBy(key: string, width: number, height: number, last: { left: number; top: number } | null) {
  void n.layoutTick.value
  const r = n.bubbleEl(key)?.getBoundingClientRect()
  if (!r) return last
  const vw = hostWindow.innerWidth
  const vh = hostWindow.innerHeight
  const left = Math.max(MARGIN, Math.min(r.left, vw - width - MARGIN))
  let top = r.bottom + GAP
  if (top + height > vh - MARGIN && r.top - GAP - height >= MARGIN) top = r.top - GAP - height
  return { left, top }
}

// ---- card ----
const cardEl = ref<HTMLElement | null>(null)
const cardNote = computed(() => (n.card.value ? n.noteOf(n.card.value.key) : null))
const cardSize = ref({ width: 320, height: 80 })
const cardPos = computed(() => (n.card.value ? placeBy(n.card.value.key, cardSize.value.width, cardSize.value.height, null) : null))
watch(cardNote, async () => {
  await nextTick()
  const el = cardEl.value
  if (el) cardSize.value = { width: el.offsetWidth, height: el.offsetHeight }
})
// A pinned card closes on a click elsewhere (not on a bubble: that one pins its own).
useEventListener(hostWindow, 'pointerdown', (e: PointerEvent) => {
  if (!n.card.value?.pinned) return
  const target = e.target as Element | null
  if (target?.closest('.kk-note-card, .kk-note-bubble')) return
  n.closeCard()
}, { capture: true })

// ---- box ----
const boxEl = ref<HTMLElement | null>(null)
const input = ref<HTMLTextAreaElement | null>(null)
const text = ref('')
let boxLast: { left: number; top: number } | null = null
const BOX_WIDTH = 300
const BOX_HEIGHT = 150
const boxPos = computed(() => {
  if (!n.box.value) return (boxLast = null)
  return (boxLast = placeBy(n.box.value.key, BOX_WIDTH, BOX_HEIGHT, boxLast))
})
const isNew = computed(() => !!n.box.value && !n.notes.value.some((x) => x.key === n.box.value!.key))
watch(n.box, async (b) => {
  if (!b) return
  text.value = n.noteOf(b.key)?.text ?? ''
  await nextTick()
  input.value?.focus()
  input.value?.setSelectionRange(text.value.length, text.value.length)
})
function save() {
  n.closeBox(text.value)
}
function onBoxKey(e: KeyboardEvent) {
  if (e.key === 'Escape') {
    e.preventDefault()
    e.stopPropagation()
    n.closeBox()
  } else if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
    e.preventDefault()
    save()
  }
}
</script>

<template>
  <Teleport :to="hostWindow.document.body">
    <div
      v-if="cardNote && cardPos && !n.box.value"
      ref="cardEl"
      class="kk-note-card"
      :class="{ pinned: n.card.value?.pinned }"
      role="tooltip"
      :style="{ left: `${cardPos.left}px`, top: `${cardPos.top}px` }"
    >
      <div v-if="cardNote.date || cardNote.author" class="meta">
        <span v-if="cardNote.author" class="author">{{ cardNote.author }}</span>
        <time v-if="cardNote.date">{{ dateFormat.format(cardNote.date) }}</time>
      </div>
      <div class="text" dir="auto">{{ cardNote.text }}</div>
      <div v-if="n.card.value?.pinned" class="buttons">
        <button type="button" class="icon-btn" :title="$t('pdfReader.notes.edit')" :aria-label="$t('pdfReader.notes.edit')" @click="n.openEdit(cardNote.key)">
          <IconEdit16Regular />
        </button>
        <button type="button" class="icon-btn" :title="$t('pdfReader.notes.delete')" :aria-label="$t('pdfReader.notes.delete')" @click="n.deleteNote(cardNote.key)">
          <IconDelete16Regular />
        </button>
      </div>
    </div>
    <div
      v-if="n.box.value && boxPos"
      ref="boxEl"
      class="kk-note-box"
      role="dialog"
      :aria-label="isNew ? $t('pdfReader.notes.add') : $t('pdfReader.notes.edit')"
      :style="{ left: `${boxPos.left}px`, top: `${boxPos.top}px`, width: `${BOX_WIDTH}px` }"
      @keydown="onBoxKey"
    >
      <textarea ref="input" v-model="text" dir="auto" rows="4" :placeholder="$t('pdfReader.notes.placeholder')" />
      <div class="buttons">
        <button type="button" class="btn" :title="$t('pdfReader.notes.cancelHint')" @click="n.closeBox()">{{ $t('pdfReader.cancel') }}</button>
        <button type="button" class="btn primary" :title="$t('pdfReader.notes.saveHint')" @click="save">{{ $t('pdfReader.notes.save') }}</button>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.kk-note-card,
.kk-note-box {
  position: fixed;
  z-index: 1000;
  box-sizing: border-box;
  background: var(--bg-secondary);
  color: var(--text-primary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  box-shadow: 0 2px 8px rgb(0 0 0 / 0.18);
  font-size: 12px;
}
.kk-note-card {
  max-width: 320px;
  min-width: 120px;
  padding: 6px 10px;
  pointer-events: none;
}
.kk-note-card.pinned {
  pointer-events: auto;
}
.meta {
  display: flex;
  gap: 6px;
  margin-bottom: 2px;
  font-size: 11px;
  color: var(--text-secondary);
}
.author {
  font-weight: 600;
}
.text {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  line-height: 1.4;
  max-height: 40vh;
  overflow: auto;
}
.buttons {
  display: flex;
  justify-content: flex-end;
  gap: 4px;
  margin-top: 6px;
}
.icon-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  padding: 0;
  border: none;
  border-radius: 4px;
  background: none;
  color: var(--text-primary);
}
.icon-btn:hover {
  background: var(--hover-bg);
}
.kk-note-box {
  padding: 8px;
}
.kk-note-box textarea {
  display: block;
  width: 100%;
  box-sizing: border-box;
  resize: vertical;
  min-height: 70px;
  padding: 6px 8px;
  font: inherit;
  font-size: 13px;
  color: var(--text-primary);
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  outline: none;
}
.kk-note-box textarea:focus {
  border-color: var(--accent-color);
}
.btn {
  height: 28px;
  padding: 0 14px;
  font-size: 12px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: var(--control-bg);
  color: var(--text-primary);
}
.btn:hover {
  background: var(--control-bg-hover);
}
.btn.primary {
  font-weight: 600;
  background: var(--accent-color);
  border-color: var(--accent-color);
  color: var(--text-on-accent, #fff);
}
</style>
