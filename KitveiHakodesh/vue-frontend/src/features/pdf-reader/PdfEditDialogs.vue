<script setup lang="ts">
import {
  IconEdit16Regular,
  IconDelete16Regular,
} from '@iconify-prerendered/vue-fluent'
import { computed, nextTick, onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import { CSSConstants } from 'pdfjs-dist'
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

// ---------------- comment dialog (PDF.js's CommentDialog) ----------------
const commentText = ref('')
const commentDialogEl = ref<HTMLDialogElement | null>(null)
const commentInput = ref<HTMLTextAreaElement | null>(null)
/** The dialog's place: fractions of the window (PDF.js's #setPosition), moved by dragging its title. */
const dialogPos = ref({ x: 0, y: 0 })
const dialogDragging = ref(false)
/** The editors' comment buttons point at the dialog (aria-controls): PDF.js needs the element. */
watch(commentDialogEl, (el) => m.setDialogElement(el), { immediate: true })
onBeforeUnmount(() => m.setDialogElement(null))
/** The page's direction: PDF.js places the dialog and popup on the comment button's inner side. */
const isLtr = () => hostWindow.getComputedStyle(props.scroller ?? hostWindow.document.body).direction !== 'rtl'
/** Text on an annotation-coloured comment: PDF.js's comment foreground. */
const tintedForeground = () => {
  const c = (CSSConstants as any).commentForegroundColor as number[] | undefined
  return c ? `rgb(${c.join(',')})` : ''
}
const commentDialogStyle = computed(() => {
  const d = m.commentDialog.value
  return {
    left: `${100 * dialogPos.value.x}%`,
    top: `${100 * dialogPos.value.y}%`,
    ...(d?.background ? { backgroundColor: d.background, borderColor: d.border ?? undefined, color: tintedForeground() } : {}),
  }
})
watch(m.commentDialog, async (d) => {
  const el = commentDialogEl.value
  if (!d) {
    if (el?.open) el.close()
    return
  }
  commentText.value = d.text
  if (!el) return
  if (commentInput.value) commentInput.value.style.height = d.height ? `${d.height}px` : ''
  // Measured hidden, then placed as PDF.js's CommentDialog.open: beside the comment button, inside the window.
  el.style.visibility = 'hidden'
  if (!el.open) el.showModal()
  await nextTick()
  const { width, height } = el.getBoundingClientRect()
  const { innerWidth, innerHeight } = hostWindow
  let x = d.x
  let y = d.y
  if (d.defaultPosition) {
    const pd = d.parent
    let buttonWidth = 0
    try { buttonWidth = (d.editor.commentButtonWidth ?? 0) * (pd?.width ?? 0) } catch { /* no comment button */ }
    if (pd) {
      if (isLtr() && x + width > Math.min(pd.x + pd.width, innerWidth)) x -= width - buttonWidth
      else if (!isLtr()) x = x - width < Math.max(0, pd.x) ? Math.max(0, x) : x - width + buttonWidth
    } else if (!isLtr()) x -= width
    const h = Math.max(height, d.height ?? 0)
    if (y + h > innerHeight) y = innerHeight - h
    if (y < 0) y = 0
  }
  x = Math.min(Math.max(0, x), Math.max(0, innerWidth - width))
  dialogPos.value = { x: Math.min(Math.max(x / innerWidth, 0), 1), y: Math.min(Math.max(y / innerHeight, 0), 1) }
  el.style.visibility = ''
  commentInput.value?.focus()
})
const commentSaveDisabled = computed(() => !m.commentDialog.value || commentText.value === m.commentDialog.value.text)
function saveComment() {
  if (!commentSaveDisabled.value) m.closeCommentDialog(commentText.value)
}
/** Drag the dialog by its title (PDF.js's toolbar drag). */
function startDialogDrag(e: PointerEvent) {
  const { innerWidth, innerHeight } = hostWindow
  let prevX = e.clientX
  let prevY = e.clientY
  e.preventDefault()
  dialogDragging.value = true
  const move = (ev: PointerEvent) => {
    dialogPos.value = { x: dialogPos.value.x + (ev.clientX - prevX) / innerWidth, y: dialogPos.value.y + (ev.clientY - prevY) / innerHeight }
    prevX = ev.clientX
    prevY = ev.clientY
  }
  const stop = () => {
    dialogDragging.value = false
    hostWindow.removeEventListener('pointermove', move)
    hostWindow.removeEventListener('pointerup', stop)
    hostWindow.removeEventListener('blur', stop)
  }
  hostWindow.addEventListener('pointermove', move)
  hostWindow.addEventListener('pointerup', stop)
  hostWindow.addEventListener('blur', stop)
}

// ---------------- comment popup (PDF.js's CommentPopup) ----------------
/** Drawn inside the page after the annotation (so it scrolls with it), placed in fractions of the page. */
const popupEl = ref<HTMLElement | null>(null)
const popupTextEl = ref<HTMLElement | null>(null)
const popupDragging = ref(false)
/** Comment dates as PDF.js's comment manager: a long date, in the app's language. */
const dateFormat = computed(() => new Intl.DateTimeFormat(locale.value, { dateStyle: 'long' }))
/** PDF.js's popup width (its stylesheet: 360px, at most the page). */
const POPUP_WIDTH = 360
const popupStyle = computed(() => {
  let { x } = m.popup
  const { y } = m.popup
  const editor = m.popup.editor
  if (m.popup.correct && editor) {
    // PDF.js's CommentPopup.#setPosition with a default position: inside the page and the window.
    const parentRect: DOMRect = editor.parentBoundingClientRect
    const widthRatio = Math.min(POPUP_WIDTH, parentRect.width) / parentRect.width
    const ltr = isLtr()
    if ((ltr && x + widthRatio > 1) || (!ltr && x - widthRatio >= 0)) {
      let buttonWidth = 0
      try { buttonWidth = editor.commentButtonWidth ?? 0 } catch { /* none */ }
      x -= widthRatio - buttonWidth
    }
    const margin = 0.01
    x = ltr ? Math.max(x, -parentRect.x / parentRect.width + margin) : Math.min(x, (hostWindow.innerWidth - parentRect.x) / parentRect.width - widthRatio - margin)
  }
  return {
    left: `${100 * x}%`,
    top: `${100 * y}%`,
    ...(m.popup.background ? { backgroundColor: m.popup.background, '--comment-fg-color': tintedForeground(), '--comment-date-fg-color': tintedForeground() } : {}),
  }
})
// A clicked (pinned) popup takes the focus, as PDF.js's.
watch(() => [m.popup.editor, m.popup.selected], async () => {
  if (!m.popup.selected) return
  await nextTick()
  popupEl.value?.focus()
})
function onPopupKeydown(e: KeyboardEvent) {
  if (e.key !== 'Escape') return
  m.hidePopup()
  e.preventDefault()
  e.stopPropagation()
}
function editFromPopup() {
  const height = popupTextEl.value ? parseFloat(hostWindow.getComputedStyle(popupTextEl.value).height) : undefined
  m.editPopupComment(height)
}
/** Drag the popup by its top (PDF.js's): its new place is kept on the annotation. */
function startPopupDrag(e: PointerEvent) {
  const editor = m.popup.editor
  if (!editor || (e.target as HTMLElement).closest('.commentPopupButtons')) return
  const { width, height } = editor.parentBoundingClientRect as DOMRect
  let prevX = e.clientX
  let prevY = e.clientY
  e.preventDefault()
  e.stopPropagation()
  popupDragging.value = true
  const move = (ev: PointerEvent) => {
    if (m.popup.correct) {
      // Leave the default place: start from where it is drawn now.
      const el = popupEl.value
      const host = m.popup.host
      if (el && host) {
        const r = el.getBoundingClientRect()
        const h = host.getBoundingClientRect()
        m.popup.x = (r.x - h.x) / h.width
        m.popup.y = (r.y - h.y - 2) / h.height
      }
      m.popup.correct = false
    }
    m.popup.x += (ev.clientX - prevX) / width
    m.popup.y += (ev.clientY - prevY) / height
    editor.commentPopupPosition = [m.popup.x, m.popup.y]
    prevX = ev.clientX
    prevY = ev.clientY
    ev.stopPropagation()
  }
  const stop = () => {
    popupDragging.value = false
    hostWindow.removeEventListener('pointermove', move, true)
    hostWindow.removeEventListener('pointerup', stop, true)
    hostWindow.removeEventListener('blur', stop)
  }
  hostWindow.addEventListener('pointermove', move, true)
  hostWindow.addEventListener('pointerup', stop, true)
  hostWindow.addEventListener('blur', stop)
}
onClickOutside(popupEl, () => m.popup.selected && m.hidePopup(), { window: hostWindow, ignore: ['.annotationCommentButton', '.comment'] })

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

    <!-- A comment's text, add or edit (PDF.js's comment dialog): beside the comment button, dragged by its title -->
    <dialog
      ref="commentDialogEl"
      class="kk-comment-dialog"
      :class="{ dragging: dialogDragging }"
      aria-labelledby="kkCommentDialogTitle"
      :style="commentDialogStyle"
      @cancel.prevent="m.closeCommentDialog()"
      @contextmenu="(e) => e.target !== commentInput && e.preventDefault()"
    >
      <template v-if="m.commentDialog.value">
        <div class="comment-dialog-title" @pointerdown="startDialogDrag">
          <span id="kkCommentDialogTitle" role="sectionhead">{{ m.commentDialog.value.text ? $t('pdfReader.edit.comment.editTitle') : $t('pdfReader.edit.comment.title') }}</span>
        </div>
        <textarea
          ref="commentInput"
          v-model="commentText"
          class="kk-input comment-input"
          dir="auto"
          :placeholder="$t('pdfReader.edit.comment.placeholder')"
          @keydown.ctrl.enter.prevent="saveComment"
          @keydown.meta.enter.prevent="saveComment"
        />
        <div class="comment-dialog-buttons">
          <button type="button" class="kk-btn" @click="m.closeCommentDialog()">{{ $t('pdfReader.cancel') }}</button>
          <button type="button" class="kk-btn primary" :disabled="commentSaveDisabled" @click="saveComment">
            {{ m.commentDialog.value.text ? $t('pdfReader.edit.comment.update') : $t('pdfReader.edit.comment.add') }}
          </button>
        </div>
      </template>
    </dialog>
  </Teleport>

  <!-- The comment popup (PDF.js's): inside the page, after the annotation - it scrolls with the page -->
  <Teleport v-if="m.popup.editor && m.popup.host" :to="m.popup.host">
    <div
      ref="popupEl"
      class="commentPopup"
      :class="{ selected: m.popup.selected, dragging: popupDragging }"
      tabindex="-1"
      role="dialog"
      aria-modal="false"
      :style="popupStyle"
      @contextmenu.prevent
      @keydown="onPopupKeydown"
      @click="popupEl?.focus()"
    >
      <div class="commentPopupTop" @pointerdown="startPopupDrag">
        <time class="commentPopupTime">{{ m.popup.date ? dateFormat.format(m.popup.date) : '' }}</time>
        <div class="commentPopupButtons" :class="{ hidden: !m.popup.editable }">
          <button type="button" class="commentPopupEdit" aria-haspopup="dialog" :title="$t('pdfReader.edit.comment.edit')" :aria-label="$t('pdfReader.edit.comment.edit')" @click="editFromPopup" @contextmenu.prevent>
            <IconEdit16Regular />
          </button>
          <button type="button" class="commentPopupDelete" :title="$t('pdfReader.edit.comment.delete')" :aria-label="$t('pdfReader.edit.comment.delete')" @click="m.deletePopupComment()" @contextmenu.prevent>
            <IconDelete16Regular />
          </button>
        </div>
      </div>
      <hr>
      <div ref="popupTextEl" class="commentPopupText"><p dir="auto">{{ m.popup.text }}</p></div>
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
/* The comment dialog: PDF.js's (360px, beside the comment button, coloured as the annotation), in the app's look. */
.kk-comment-dialog {
  position: fixed;
  inset: auto;
  margin: 0;
  width: 360px;
  max-width: 100%;
  min-width: 200px;
  box-sizing: border-box;
  padding: 8px 16px 16px;
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 4px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 0.5px 2px rgb(0 0 0 / 0.1), 0 4px 16px rgb(0 0 0 / 0.2);
  color: var(--text-primary);
  font-size: 12px;
}
.kk-comment-dialog:not([open]) {
  display: none;
}
.kk-comment-dialog::backdrop {
  background: transparent;
}
.kk-comment-dialog.dragging,
.kk-comment-dialog.dragging * {
  cursor: move !important;
}
.comment-dialog-title {
  display: flex;
  align-items: center;
  height: 32px;
  font-size: 14px;
  font-weight: 600;
  cursor: move;
  user-select: none;
}
.comment-input {
  width: 100%;
  box-sizing: border-box;
  min-height: 132px;
  margin-bottom: 12px;
  padding: 6px 8px;
  resize: vertical;
  scrollbar-width: thin;
  font-family: inherit;
}
.comment-dialog-buttons {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
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

/* The comment popup: PDF.js's stylesheet (pdf_viewer.css .commentPopup) in the app's colours; Fluent icons. */
.commentPopup {
  --comment-bg-color: var(--bg-secondary);
  --comment-fg-color: var(--text-primary);
  --comment-border-color: var(--border-color);
  --comment-date-fg-color: var(--text-secondary);
  --divider-color: var(--border-color);
  --button-comment-color: var(--text-primary);
  --button-comment-hover-bg: var(--hover-bg);
  --button-comment-active-bg: var(--hover-bg);
  --comment-focus-outline-color: var(--accent-color, currentColor);
}
.commentPopup .commentPopupButtons.hidden {
  display: none;
}
.commentPopup .commentPopupButtons > button {
  display: flex;
  align-items: center;
  justify-content: center;
}
.commentPopup .commentPopupButtons > button::before {
  display: none;
}
.commentPopup .commentPopupText {
  max-height: 50vh;
  overflow-y: auto;
  scrollbar-width: thin;
  overflow-wrap: anywhere;
}
</style>
