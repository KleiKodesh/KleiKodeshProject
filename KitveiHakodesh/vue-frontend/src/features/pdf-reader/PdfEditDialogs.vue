<script setup lang="ts">
import {
  IconDismiss16Regular,
  IconEdit16Regular,
  IconDelete16Regular,
} from '@iconify-prerendered/vue-fluent'
import { computed, nextTick, ref, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { onClickOutside, useEventListener } from '@vueuse/core'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { SIGNATURE_HEIGHT_IN_PAGE, type PdfEditManagers } from './pdfEditManagers'

/**
 * The dialogs PDF.js's editors ask for, in the app's look (PDF.js's own are in its app, not in
 * pdfjs-dist): new signature (type / draw / image), a signature's description, a comment's text,
 * and the comment popup over the page. State: pdfEditManagers.ts.
 */
const props = defineProps<{ managers: PdfEditManagers; scroller: HTMLElement | null }>()
const m = props.managers
const hostWindow = useHostWindow()
const { locale, t } = useI18n()

/* eslint-disable @typescript-eslint/no-explicit-any */

// ---------------- signature dialog ----------------
type Tab = 'type' | 'draw' | 'image'
const TABS: Tab[] = ['type', 'draw', 'image']
/** Handwriting-like fonts of Windows, and plain ones that hold Hebrew. */
const FONTS = ['Segoe Script', 'Ink Free', 'Gabriola', 'Guttman Yad-Brush', 'David', 'Times New Roman']
const tab = ref<Tab>('type')
const typed = ref('')
const font = ref(FONTS[0])
const description = ref('')
const descriptionTouched = ref(false)
const saveIt = ref(true)
const error = ref('')

// Drawing pad (as PDF.js's: an SVG, curves in its pixels).
const pad = ref<SVGSVGElement | null>(null)
const thickness = ref(2)
const drawn = shallowRef<{ width: number; height: number; thickness: number; curves: { points: number[] }[] } | null>(null)
const drawPath = ref('')
let drawing: number[] | null = null

// Picked image: PDF.js extracts the signature's contours from it.
const imageData = shallowRef<any>(null)
const imagePreview = shallowRef<{ viewBox: string; path: string } | null>(null)
const imageInput = ref<HTMLInputElement | null>(null)
const busy = ref(false)

watch(m.signatureDialog, (d) => {
  if (!d) return
  tab.value = 'type'
  typed.value = ''
  description.value = ''
  descriptionTouched.value = false
  saveIt.value = m.canSaveMore()
  error.value = ''
  drawn.value = null
  drawPath.value = ''
  imageData.value = null
  imagePreview.value = null
})

function defaultDescription() {
  if (descriptionTouched.value) return
  if (tab.value === 'type') description.value = typed.value
  else if (tab.value === 'draw' && drawn.value) description.value = description.value || t('pdfReader.edit.signature.defaultDescription')
}
watch([typed, tab], defaultDescription)

function padPoint(ev: PointerEvent): [number, number] {
  const r = pad.value!.getBoundingClientRect()
  return [Math.round(ev.clientX - r.left), Math.round(ev.clientY - r.top)]
}
function padDown(ev: PointerEvent) {
  if (ev.button !== 0 || !pad.value) return
  ev.preventDefault()
  pad.value.setPointerCapture(ev.pointerId)
  const r = pad.value.getBoundingClientRect()
  if (!drawn.value) drawn.value = { width: r.width, height: r.height, thickness: thickness.value, curves: [] }
  const [x, y] = padPoint(ev)
  drawing = [x, y]
  drawn.value.curves.push({ points: drawing })
  drawPath.value += `M${x} ${y}`
  defaultDescription()
}
function padMove(ev: PointerEvent) {
  if (!drawing || !drawn.value) return
  const [x, y] = padPoint(ev)
  if (x < 0 || y < 0 || x > drawn.value.width || y > drawn.value.height) return
  if (x === drawing.at(-2) && y === drawing.at(-1)) return
  drawing.push(x, y)
  drawPath.value += `L${x} ${y}`
}
function padUp(ev: PointerEvent) {
  if (!drawing) return
  pad.value?.releasePointerCapture(ev.pointerId)
  if (drawing.length === 2) drawPath.value += `L${drawing[0]} ${drawing[1]}`
  drawing = null
}
function clearPad() {
  drawn.value = null
  drawPath.value = ''
}
watch(thickness, (v) => drawn.value && (drawn.value.thickness = v))

async function pickImage(ev: Event) {
  const file = (ev.target as HTMLInputElement).files?.[0]
  const d = m.signatureDialog.value
  if (!file || !d) return
  error.value = ''
  busy.value = true
  try {
    const data = await d.uiManager.imageManager.getFromFile(file)
    const lines = data && d.editor.getFromImage(data.bitmap)
    if (!lines) {
      error.value = t('pdfReader.edit.signature.noData')
      return
    }
    imageData.value = lines
    imagePreview.value = { viewBox: lines.outline.viewBox, path: lines.outline.toSVGPath() }
    if (!descriptionTouched.value) description.value = file.name
  } catch {
    error.value = t('pdfReader.edit.signature.imageFailed')
  } finally {
    busy.value = false
  }
}

const canAdd = computed(() =>
  tab.value === 'type' ? !!typed.value.trim() : tab.value === 'draw' ? !!drawn.value?.curves.length : !!imageData.value)

async function addSignature() {
  const d = m.signatureDialog.value
  if (!d || !canAdd.value) return
  let data: any
  if (tab.value === 'type') data = d.editor.getFromText(typed.value, { fontFamily: `"${font.value}"`, fontStyle: 'normal', fontWeight: 'normal' })
  else if (tab.value === 'draw') data = d.editor.getDrawnSignature(drawn.value)
  else data = imageData.value
  if (!data) {
    error.value = t('pdfReader.edit.signature.noData')
    return
  }
  const desc = description.value.trim()
  const uuid = saveIt.value && m.canSaveMore() ? await m.saveSignature(data, desc) : null
  d.editor.addSignature(data, SIGNATURE_HEIGHT_IN_PAGE, desc, uuid)
  m.closeSignatureDialog()
}

// ---------------- signature description ----------------
const descText = ref('')
watch(m.descriptionDialog, (d) => d && (descText.value = d.editor.description ?? ''))

// ---------------- comment dialog ----------------
const commentText = ref('')
const commentInput = ref<HTMLTextAreaElement | null>(null)
watch(m.commentDialog, async (d) => {
  if (!d) return
  commentText.value = d.text
  await nextTick()
  commentInput.value?.focus()
})

// ---------------- comment popup ----------------
const popupEl = ref<HTMLElement | null>(null)
const dateFormat = computed(() => new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'short' }))
const popupStyle = computed(() => {
  const w = 240
  const x = Math.min(Math.max(4, m.popup.x), hostWindow.innerWidth - w - 4)
  const y = Math.min(Math.max(4, m.popup.y), hostWindow.innerHeight - 60)
  return { left: `${x}px`, top: `${y}px`, width: `${w}px` }
})
onClickOutside(popupEl, () => m.popup.selected && m.hidePopup(), { window: hostWindow, ignore: ['.annotationCommentButton'] })
useEventListener(() => props.scroller, 'scroll', () => m.popup.editor && m.hidePopup(), { passive: true })

// Escape closes the top dialog (or the popup).
useEventListener(hostWindow, 'keydown', (e: KeyboardEvent) => {
  if (e.code !== 'Escape') return
  if (m.commentDialog.value) m.closeCommentDialog()
  else if (m.descriptionDialog.value) m.closeDescriptionDialog()
  else if (m.signatureDialog.value) m.closeSignatureDialog()
  else if (m.popup.editor) m.hidePopup()
  else return
  e.preventDefault()
  e.stopPropagation()
}, { capture: true })
</script>

<template>
  <Teleport :to="hostWindow.document.body">
    <!-- New signature -->
    <div v-if="m.signatureDialog.value" class="kk-backdrop" @click.self="m.closeSignatureDialog()">
      <div class="kk-dialog sig-dialog" role="dialog" aria-modal="true" :aria-label="$t('pdfReader.edit.signature.title')">
        <h3 class="kk-title">{{ $t('pdfReader.edit.signature.title') }}</h3>
        <div class="tabs" role="tablist">
          <button
            v-for="name in TABS"
            :key="name"
            role="tab"
            class="tab"
            :class="{ active: tab === name }"
            :aria-selected="tab === name"
            @click="(tab = name), (error = '')"
          >{{ $t(`pdfReader.edit.signature.tab.${name}`) }}</button>
        </div>

        <div v-if="tab === 'type'" class="tab-body">
          <input
            v-model="typed"
            class="kk-input type-input"
            dir="auto"
            autofocus
            :style="{ fontFamily: `'${font}'` }"
            :placeholder="$t('pdfReader.edit.signature.typePlaceholder')"
          />
          <label class="row">
            <span>{{ $t('pdfReader.edit.signature.font') }}</span>
            <select v-model="font" class="kk-input">
              <option v-for="f in FONTS" :key="f" :value="f" :style="{ fontFamily: `'${f}'` }">{{ f }}</option>
            </select>
          </label>
        </div>

        <div v-else-if="tab === 'draw'" class="tab-body">
          <svg
            ref="pad"
            class="pad"
            @pointerdown="padDown"
            @pointermove="padMove"
            @pointerup="padUp"
            @pointercancel="padUp"
            @contextmenu.prevent
          >
            <path :d="drawPath" :stroke-width="thickness" />
            <text v-if="!drawPath" x="50%" y="50%" text-anchor="middle" dominant-baseline="middle" class="pad-hint">{{ $t('pdfReader.edit.signature.drawHint') }}</text>
          </svg>
          <div class="row">
            <label class="row">
              <span>{{ $t('pdfReader.edit.thickness') }}</span>
              <input v-model.number="thickness" type="range" min="1" max="5" step="1" />
            </label>
            <button class="kk-btn" :disabled="!drawPath" @click="clearPad">{{ $t('pdfReader.edit.signature.clear') }}</button>
          </div>
        </div>

        <div v-else class="tab-body">
          <div class="image-box" :class="{ waiting: busy }">
            <svg v-if="imagePreview" class="preview contours" :viewBox="imagePreview.viewBox" preserveAspectRatio="xMidYMid meet"><path :d="imagePreview.path" /></svg>
            <button v-else class="kk-btn" @click="imageInput?.click()">{{ $t('pdfReader.edit.signature.pickImage') }}</button>
          </div>
          <input ref="imageInput" type="file" accept="image/*" hidden @change="pickImage" />
          <button v-if="imagePreview" class="kk-btn" @click="imageInput?.click()">{{ $t('pdfReader.edit.signature.otherImage') }}</button>
        </div>

        <div v-if="error" class="error">{{ error }}</div>

        <label class="col">
          <span>{{ $t('pdfReader.edit.signature.description') }}</span>
          <input v-model="description" class="kk-input" dir="auto" @input="descriptionTouched = true" />
        </label>
        <label class="row check">
          <input v-model="saveIt" type="checkbox" :disabled="!m.canSaveMore()" />
          <span>{{ m.canSaveMore() ? $t('pdfReader.edit.signature.save') : $t('pdfReader.edit.signature.storageFull') }}</span>
        </label>

        <div class="kk-actions">
          <button class="kk-btn primary" :disabled="!canAdd || busy" @click="addSignature">{{ $t('pdfReader.edit.signature.add') }}</button>
          <button class="kk-btn" @click="m.closeSignatureDialog()">{{ $t('pdfReader.cancel') }}</button>
        </div>
      </div>
    </div>

    <!-- A placed signature's description -->
    <div v-if="m.descriptionDialog.value" class="kk-backdrop" @click.self="m.closeDescriptionDialog()">
      <div class="kk-dialog" role="dialog" aria-modal="true" :aria-label="$t('pdfReader.edit.signature.editDescription')">
        <h3 class="kk-title">{{ $t('pdfReader.edit.signature.editDescription') }}</h3>
        <input v-model="descText" class="kk-input" dir="auto" autofocus @keydown.enter="m.closeDescriptionDialog(descText)" />
        <div class="kk-actions">
          <button class="kk-btn primary" @click="m.closeDescriptionDialog(descText)">{{ $t('pdfReader.edit.comment.save') }}</button>
          <button class="kk-btn" @click="m.closeDescriptionDialog()">{{ $t('pdfReader.cancel') }}</button>
        </div>
      </div>
    </div>

    <!-- A comment's text (new note, or edit) -->
    <div v-if="m.commentDialog.value" class="kk-backdrop" @click.self="m.closeCommentDialog()">
      <div class="kk-dialog" role="dialog" aria-modal="true" :aria-label="$t('pdfReader.edit.comment.title')">
        <h3 class="kk-title">{{ m.commentDialog.value.text ? $t('pdfReader.edit.comment.editTitle') : $t('pdfReader.edit.comment.title') }}</h3>
        <textarea
          ref="commentInput"
          v-model="commentText"
          class="kk-input comment-input"
          dir="auto"
          :placeholder="$t('pdfReader.edit.comment.placeholder')"
          @keydown.ctrl.enter.prevent="m.closeCommentDialog(commentText)"
        />
        <div class="kk-actions">
          <button class="kk-btn primary" :disabled="!commentText.trim() && !m.commentDialog.value.text" @click="m.closeCommentDialog(commentText)">{{ $t('pdfReader.edit.comment.save') }}</button>
          <button class="kk-btn" @click="m.closeCommentDialog()">{{ $t('pdfReader.cancel') }}</button>
        </div>
      </div>
    </div>

    <!-- The comment popup over the page -->
    <div
      v-if="m.popup.editor"
      ref="popupEl"
      class="kk-comment-popup"
      :class="{ selected: m.popup.selected }"
      role="dialog"
      :style="popupStyle"
    >
      <div class="popup-top">
        <time v-if="m.popup.date" class="popup-date">{{ dateFormat.format(m.popup.date) }}</time>
        <span class="popup-buttons">
          <template v-if="m.popup.editable && m.popup.selected">
            <button :title="$t('pdfReader.edit.comment.edit')" @click="m.editPopupComment()"><IconEdit16Regular /></button>
            <button :title="$t('pdfReader.edit.comment.delete')" @click="m.deletePopupComment()"><IconDelete16Regular /></button>
          </template>
          <button v-if="m.popup.selected" :title="$t('pdfViewer.closeEsc')" @click="m.hidePopup()"><IconDismiss16Regular /></button>
        </span>
      </div>
      <div class="popup-text" dir="auto">{{ m.popup.text }}</div>
    </div>
  </Teleport>
</template>

<style scoped>
/* The app's dialog look (components/AlertDialog.vue), as PdfPrintChoiceDialog. */
.kk-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 10000;
}
.kk-dialog {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 20px 20px 14px;
  width: 420px;
  max-width: calc(100vw - 32px);
  max-height: 85vh;
  overflow: auto;
  scrollbar-width: thin;
  display: flex;
  flex-direction: column;
  gap: 10px;
  font-size: 12px;
  color: var(--text-primary);
}
.sig-dialog {
  width: 480px;
}
.kk-title {
  margin: 0;
  font-size: 14px;
}
.kk-input {
  height: 28px;
  padding: 0 8px;
  font-size: 12px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: var(--control-bg);
  color: var(--text-primary);
}
.type-input {
  height: 64px;
  font-size: 32px;
}
.comment-input {
  height: 120px;
  padding: 6px 8px;
  resize: vertical;
  scrollbar-width: thin;
  font-family: inherit;
}
.kk-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
  padding-top: 10px;
  border-top: 1px solid var(--border-color);
}
.kk-btn {
  height: 30px;
  padding: 0 14px;
  font-size: 12px;
  border: 1px solid var(--border-color);
  background: var(--control-bg);
  color: var(--text-primary);
  border-radius: 4px;
}
.kk-btn:hover:not(:disabled) {
  background: var(--control-bg-hover);
}
.kk-btn:disabled {
  opacity: 0.5;
}
.kk-btn.primary {
  font-weight: 600;
}
.tabs {
  display: flex;
  gap: 4px;
  border-bottom: 1px solid var(--border-color);
}
.tab {
  padding: 6px 12px;
  font-size: 12px;
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  color: var(--text-primary);
}
.tab.active {
  border-bottom-color: var(--accent-color);
  font-weight: 600;
}
.tab-body {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.col {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.check {
  justify-content: flex-start;
}
.pad {
  width: 100%;
  height: 160px;
  border: 1px dashed var(--border-color);
  border-radius: 4px;
  background: #fff;
  touch-action: none;
  cursor: crosshair;
}
.pad path {
  fill: none;
  stroke: #000;
  stroke-linecap: round;
  stroke-linejoin: round;
}
.pad-hint {
  fill: #888;
  font-size: 12px;
  pointer-events: none;
}
.image-box {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 160px;
  border: 1px dashed var(--border-color);
  border-radius: 4px;
  background: #fff;
}
.image-box.waiting {
  cursor: wait;
  opacity: 0.6;
}
.preview {
  width: 100%;
  height: 100%;
}
.preview.contours path {
  fill: #000;
}
.error {
  color: var(--error-color, #c42b1c);
}

.kk-comment-popup {
  position: fixed;
  z-index: 9500;
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 8px 10px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.18);
  font-size: 12px;
  color: var(--text-primary);
  pointer-events: none;
}
.kk-comment-popup.selected {
  pointer-events: auto;
}
.popup-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 6px;
  min-height: 22px;
}
.popup-date {
  font-size: 10.5px;
  color: var(--text-secondary);
}
.popup-buttons {
  display: flex;
  gap: 2px;
  margin-inline-start: auto;
}
.popup-buttons button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  padding: 3px;
  border-radius: 4px;
  background: none;
}
.popup-buttons button:hover {
  background: var(--hover-bg);
}
.popup-text {
  max-height: 200px;
  overflow-y: auto;
  scrollbar-width: thin;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  line-height: 1.4;
}
</style>
