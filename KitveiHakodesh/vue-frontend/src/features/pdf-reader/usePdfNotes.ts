import { computed, ref, shallowRef, watch, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { AnnotationEditorType, PDFDateString, type PDFDocumentProxy } from 'pdfjs-dist'
import { t } from '@/i18n'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import type { PdfScroller } from './usePdfScroller'

/**
 * Notes on the pages: small speech bubbles, saved in the PDF as standard sticky notes (Text
 * annotations, written by PDF.js's own save: stage()). The file's own Text annotations (from any PDF tool) show the
 * same way; PDF.js's own drawing of them is hidden (pdfReaderTheme.css).
 *
 * - Note mode (toolbar "Add note", Alt+N): a speech-bubble cursor; a click on a page places a
 *   note there (its tip at the click) and opens the note box; then the mode turns off.
 * - Hovering a bubble shows its text (the card); a click pins the card with Edit / Delete.
 * - Add / edit / delete are steps in PDF.js's undo stack (Ctrl+Z / Ctrl+Y).
 * Bubbles live in a layer of our own in each PDF.js page box (`.kk-notes-layer`), placed in
 * percents of the page, so zoom moves them; the card and the box are PdfNotesOverlay.vue.
 */

/** A note's size on the page in PDF points (the bubble: 20 px at 100%). */
export const NOTE_SIZE = 20
/** The bubble's tip in the Fluent comment icon (20 x 20 box): where a click places it. */
const TIP_X = 5.4 / 20
const TIP_Y = 17.8 / 20
const DRAFT_KEY = '__draft'

export interface PdfNote {
  key: string
  pageIndex: number
  /** PDF user-space rect [x1, y1, x2, y2]; the bubble's top-left is at (x1, y2). */
  rect: number[]
  text: string
  date: Date | null
  author?: string
  /** The annotation object in the file (notes read from the file). */
  ref?: PdfRef
  /** Its PDF.js id ("12R") and its popup's, removed with it. */
  id?: string
  popupRef?: string
  /** The text as the file holds it (notes read from the file). */
  fileText?: string
  deleted?: boolean
}

/* eslint-disable @typescript-eslint/no-explicit-any */
type PageView = any

/** The Fluent comment icon (20 x 20): filled bubble, and its outline. */
export const BUBBLE_FILL = 'M6.799 17.803a1.01 1.01 0 0 1-1.4-.199a.98.98 0 0 1-.199-.59v-2.172h-.6c-1.436 0-2.6-1.149-2.6-2.566v-6.71C2 4.149 3.164 3 4.6 3h10.8C16.836 3 18 4.149 18 5.566v6.71c0 1.418-1.164 2.566-2.6 2.566h-4.59z'
export const BUBBLE_RING = 'M10.48 13.842h4.92c.896 0 1.6-.713 1.6-1.566v-6.71C17 4.713 16.296 4 15.4 4H4.6C3.704 4 3 4.713 3 5.566v6.71c0 .853.704 1.566 1.6 1.566h1.6V17h.003l.002-.001zM6.8 17.803a1.01 1.01 0 0 1-1.4-.199a.98.98 0 0 1-.199-.59v-2.172h-.6c-1.436 0-2.6-1.149-2.6-2.566v-6.71C2 4.149 3.164 3 4.6 3h10.8C16.836 3 18 4.149 18 5.566v6.71c0 1.418-1.164 2.566-2.6 2.566h-4.59z'

export interface PdfRef { num: number; gen: number }

/** "12R" / "12R3" (PDF.js annotation ids) to the object reference. */
function refOfId(id: string): PdfRef | undefined {
  const m = /^(\d+)R(\d*)$/.exec(id)
  return m ? { num: Number(m[1]), gen: Number(m[2] || 0) } : undefined
}
/** PDF.js's AnnotationType.TEXT: our fork's save writes these as sticky notes (/Subtype /Text). */
const TEXT_ANNOTATION = 1
/** PDF.js saves annotationStorage entries under this key prefix (AnnotationEditorPrefix). */
const STORAGE_PREFIX = 'pdfjs_internal_editor_kk-note-'

export function usePdfNotes(pdf: PdfScroller, container: Ref<HTMLElement | null>) {
  const hostWindow = useHostWindow()
  const notes = shallowRef<PdfNote[]>([])
  const visible = computed(() => notes.value.filter((n) => !n.deleted))
  /** A note placed but not saved yet (the box is open for it). */
  const draft = shallowRef<{ pageIndex: number; rect: number[] } | null>(null)
  /** The note box: a note's key, or the draft's. */
  const box = shallowRef<{ key: string } | null>(null)
  /** The card: shown on hover, pinned by a click. */
  const card = shallowRef<{ key: string; pinned: boolean } | null>(null)
  const noteMode = ref(false)
  /** Bumped when bubbles may have moved (render, scroll, zoom): the overlay re-measures. */
  const layoutTick = ref(0)
  let loadedPages = new Set<number>()
  let loadingAll: Promise<void> | null = null
  let nextId = 0

  const dirty = computed(() => notes.value.some((n) => (n.ref ? n.deleted || n.text !== n.fileText : !n.deleted)))

  watch(pdf.pdfDocument, () => {
    notes.value = []
    draft.value = null
    box.value = null
    card.value = null
    loadedPages = new Set()
    loadingAll = null
  })

  // ---- the file's notes ----
  function addFromFile(pageIndex: number, annots: any[]) {
    if (loadedPages.has(pageIndex)) return
    loadedPages.add(pageIndex)
    const known = new Set(notes.value.map((n) => n.key))
    const found: PdfNote[] = []
    for (const a of annots) {
      if (a.subtype !== 'Text' || a.inReplyTo || known.has(a.id)) continue
      const text = (a.contentsObj?.str || a.richText?.str || '').trim()
      const raw = a.modificationDate || a.creationDate
      found.push({
        key: a.id, pageIndex, rect: a.rect, text, fileText: text, ref: refOfId(a.id), id: a.id, popupRef: a.popupRef || undefined,
        author: a.titleObj?.str || undefined,
        date: (raw && PDFDateString.toDateObject(raw)) || null,
      })
    }
    if (found.length) notes.value = [...notes.value, ...found]
  }
  async function loadPage(pageIndex: number, doc: PDFDocumentProxy) {
    if (loadedPages.has(pageIndex)) return
    const page = await doc.getPage(pageIndex + 1)
    const annots = await page.getAnnotations({ intent: 'display' })
    if (pdf.pdfDocument.value === doc) addFromFile(pageIndex, annots)
  }
  /** Every page's notes (the side panel's list). */
  function loadAll(): Promise<void> {
    const doc = pdf.pdfDocument.value
    if (!doc) return Promise.resolve()
    return (loadingAll ??= (async () => {
      for (let i = 0; i < doc.numPages && pdf.pdfDocument.value === doc; i++) await loadPage(i, doc).catch(() => {})
    })())
  }

  // ---- bubbles on the pages ----
  const pageView = (i: number): PageView => (pdf.viewer.value as any)?.getPageView(i)
  function bubble(doc: Document, n: { key: string; rect: number[] }, view: PageView, isDraft = false): HTMLElement {
    const vp = view.viewport
    const [x, y] = vp.convertToViewportPoint(n.rect[0], n.rect[3])
    const b = doc.createElement('button')
    b.type = 'button'
    b.className = isDraft ? 'kk-note-bubble draft' : 'kk-note-bubble'
    b.dataset.noteKey = n.key
    b.style.left = `${(x / vp.width) * 100}%`
    b.style.top = `${(y / vp.height) * 100}%`
    b.setAttribute('aria-label', t('pdfReader.notes.note'))
    b.innerHTML = `<svg viewBox="0 0 20 20" aria-hidden="true"><path class="fill" d="${BUBBLE_FILL}"/><path class="ring" d="${BUBBLE_RING}"/></svg>`
    return b
  }
  function renderPage(i: number) {
    const view = pageView(i)
    const div: HTMLElement | undefined = view?.div
    if (!div || !view.viewport) return
    let layer = div.querySelector<HTMLElement>(':scope > .kk-notes-layer')
    const items = visible.value.filter((n) => n.pageIndex === i)
    const d = draft.value?.pageIndex === i ? draft.value : null
    if (!items.length && !d) return void layer?.remove()
    if (!layer) {
      layer = div.ownerDocument.createElement('div')
      layer.className = 'kk-notes-layer'
    }
    // Always last in the page: PDF.js adds its layers as it renders.
    if (layer !== div.lastElementChild) div.append(layer)
    const doc = div.ownerDocument
    layer.replaceChildren(...items.map((n) => bubble(doc, n, view)), ...(d ? [bubble(doc, { key: DRAFT_KEY, rect: d.rect }, view, true)] : []))
  }
  const bump = () => { layoutTick.value++ }
  function onPageDrawn(e: { pageNumber: number }) {
    const i = e.pageNumber - 1
    const doc = pdf.pdfDocument.value
    renderPage(i)
    bump()
    if (doc && !loadedPages.has(i)) void loadPage(i, doc).catch(() => {})
  }
  pdf.eventBus.on('pagerendered', onPageDrawn)
  pdf.eventBus.on('annotationlayerrendered', onPageDrawn)
  pdf.eventBus.on('scalechanging', bump)
  pdf.eventBus.on('rotationchanging', bump)
  useEventListener(container, 'scroll', bump, { passive: true })
  useEventListener(hostWindow, 'resize', bump, { passive: true })

  // Re-draw the pages whose notes changed.
  watch([notes, draft], ([now, d], [before, dBefore]) => {
    const pages = new Set<number>()
    const old = new Map((before ?? []).map((n) => [n.key, n]))
    for (const n of now) if (old.get(n.key) !== n) pages.add(n.pageIndex)
    const kept = new Set(now.map((n) => n.key))
    for (const n of before ?? []) if (!kept.has(n.key) || now.find((x) => x.key === n.key)?.pageIndex !== n.pageIndex) pages.add(n.pageIndex)
    if (d) pages.add(d.pageIndex)
    if (dBefore) pages.add(dBefore.pageIndex)
    for (const p of pages) renderPage(p)
    bump()
  })

  // ---- changes (each one an undo step in PDF.js's stack) ----
  function update(key: string, patch: Partial<PdfNote>) {
    notes.value = notes.value.map((n) => (n.key === key ? { ...n, ...patch } : n))
  }
  function step(cmd: () => void, undo: () => void) {
    const ui = pdf.editManagers.getUiManager()
    if (ui?.addCommands) ui.addCommands({ cmd, undo, mustExec: true })
    else cmd()
  }
  function addNote(pageIndex: number, rect: number[], text: string) {
    const note: PdfNote = { key: `kk-note-${++nextId}`, pageIndex, rect, text, date: new Date() }
    step(
      () => { notes.value = [...notes.value.filter((n) => n.key !== note.key), note] },
      () => { notes.value = notes.value.filter((n) => n.key !== note.key); if (card.value?.key === note.key) card.value = null },
    )
  }
  function editNote(key: string, text: string) {
    const n = notes.value.find((x) => x.key === key)
    if (!n || n.text === text) return
    const before = { text: n.text, date: n.date }
    const after = { text, date: new Date() }
    step(() => update(key, after), () => update(key, before))
  }
  function deleteNote(key: string) {
    if (card.value?.key === key) card.value = null
    step(
      () => { update(key, { deleted: true }); if (card.value?.key === key) card.value = null },
      () => update(key, { deleted: false }),
    )
  }

  // ---- placing a note ----
  function setNoteMode(on: boolean) {
    noteMode.value = on
    // Note mode replaces any editor: reading mode underneath.
    const v = pdf.viewer.value
    // (the getter gives the mode number; the setter takes { mode })
    if (on && v && (v.annotationEditorMode as unknown as number) !== AnnotationEditorType.NONE) v.annotationEditorMode = { mode: AnnotationEditorType.NONE }
  }
  const toggleNoteMode = () => setNoteMode(!noteMode.value)
  pdf.eventBus.on('annotationeditormodechanged', (e: { mode: number }) => {
    if (e.mode !== AnnotationEditorType.NONE) noteMode.value = false
  })
  watch([noteMode, container], ([on, el]) => el?.classList.toggle('kk-note-mode', on), { immediate: true })

  /** A new note with its tip at window point (x, y) on `pageDiv`; opens the note box. */
  function placeAt(pageDiv: HTMLElement, clientX: number, clientY: number) {
    const pageIndex = Number(pageDiv.dataset.pageNumber) - 1
    const view = pageView(pageIndex)
    const vp = view?.viewport
    if (!vp || Number.isNaN(pageIndex)) return
    const r = pageDiv.getBoundingClientRect()
    const vx = ((clientX - r.left - pageDiv.clientLeft) / pageDiv.clientWidth) * vp.width
    const vy = ((clientY - r.top - pageDiv.clientTop) / pageDiv.clientHeight) * vp.height
    const s = NOTE_SIZE * vp.scale
    const [ax, ay] = vp.convertToPdfPoint(vx - TIP_X * s, vy - TIP_Y * s)
    card.value = null
    draft.value = { pageIndex, rect: [ax, ay - NOTE_SIZE, ax + NOTE_SIZE, ay] }
    box.value = { key: DRAFT_KEY }
  }
  // Note mode: a click on a page places the note (capture: before links and the text layer).
  useEventListener(container, 'click', (e: MouseEvent) => {
    if (!noteMode.value || e.button !== 0) return
    const target = e.target as HTMLElement | null
    if (!target || target.closest('.kk-note-bubble')) return
    const page = target.closest<HTMLElement>('.page')
    if (!page) return
    e.preventDefault()
    e.stopPropagation()
    noteMode.value = false // one note per click
    placeAt(page, e.clientX, e.clientY)
  }, { capture: true })

  /** "Add note" for the current selection: a note at its end (no highlight), or null when none. */
  function noteAtSelection(): (() => void) | null {
    const sel = hostWindow.getSelection()
    const el = container.value
    if (!sel || sel.rangeCount === 0 || sel.isCollapsed || !el) return null
    const range = sel.getRangeAt(0)
    const end = range.endContainer.nodeType === Node.TEXT_NODE ? range.endContainer.parentElement : (range.endContainer as Element)
    const page = end?.closest<HTMLElement>('.page')
    if (!page || !el.contains(page)) return null
    const rects = [...range.getClientRects()].filter((r) => r.width > 0 && r.height > 0)
    const last = rects[rects.length - 1]
    if (!last) return null
    // The selection's end: the line's left end in right-to-left text, else its right end; the
    // bubble's tip sits on top of the line there.
    const rtl = /[֐-ࣿיִ-﷿ﹰ-ﻼ]/.test(range.toString())
    const x = rtl ? last.left : last.right
    const y = last.top
    return () => {
      hostWindow.getSelection()?.removeAllRanges()
      placeAt(page, x, y)
    }
  }

  // ---- the card and the box ----
  function bubbleKey(e: Event): string | null {
    return (e.target as HTMLElement | null)?.closest<HTMLElement>('.kk-note-bubble')?.dataset.noteKey ?? null
  }
  useEventListener(container, 'pointerover', (e: PointerEvent) => {
    const key = bubbleKey(e)
    if (key && key !== DRAFT_KEY && !card.value?.pinned && !box.value) card.value = { key, pinned: false }
  })
  useEventListener(container, 'pointerout', (e: PointerEvent) => {
    const key = bubbleKey(e)
    // Moving between the bubble's own parts is not leaving it.
    const to = (e.relatedTarget as Element | null)?.closest?.('.kk-note-bubble') as HTMLElement | null | undefined
    if (to?.dataset.noteKey === key) return
    if (key && card.value?.key === key && !card.value.pinned) card.value = null
  })
  useEventListener(container, 'click', (e: MouseEvent) => {
    const key = bubbleKey(e)
    if (!key || key === DRAFT_KEY) return
    e.preventDefault()
    e.stopPropagation()
    card.value = card.value?.key === key && card.value.pinned ? null : { key, pinned: true }
  })
  // Esc: the pinned card, then note mode (the box handles its own Esc).
  useEventListener(hostWindow, 'keydown', (e: KeyboardEvent) => {
    if (e.key !== 'Escape' || box.value) return
    if (card.value?.pinned) card.value = null
    else if (noteMode.value) noteMode.value = false
    else return
    e.preventDefault()
    e.stopPropagation()
  }, { capture: true })

  function pin(key: string) {
    box.value = null
    card.value = { key, pinned: true }
    bump()
  }
  function openEdit(key: string) {
    card.value = null
    box.value = { key }
  }
  /** Close the box: save `text` (undefined = cancel). Empty text: no note (an existing one is removed). */
  function closeBox(text?: string) {
    const b = box.value
    if (!b) return
    box.value = null
    const value = text?.trim()
    if (b.key === DRAFT_KEY) {
      const d = draft.value
      draft.value = null
      if (d && value) addNote(d.pageIndex, d.rect, value)
      return
    }
    if (value === undefined) return
    if (value) editNote(b.key, value)
    else deleteNote(b.key)
  }

  /** The note with this key (the draft too). */
  function noteOf(key: string): { key: string; text: string; date: Date | null; author?: string } | null {
    if (key === DRAFT_KEY) return draft.value ? { key, text: '', date: null } : null
    return visible.value.find((n) => n.key === key) ?? null
  }
  /** The bubble element of a note on its page, when drawn. */
  function bubbleEl(key: string): HTMLElement | null {
    return container.value?.querySelector<HTMLElement>(`.kk-note-bubble[data-note-key="${CSS.escape(key)}"]`) ?? null
  }

  // ---- saving ----
  /**
   * Puts the notes' changes into the document's annotationStorage, so PDF.js's saveDocument()
   * writes them in its own update section (our fork saves AnnotationType.TEXT entries as sticky
   * notes, encrypted files too). Returns the undo, for a save that failed.
   */
  function stage(doc: PDFDocumentProxy): () => void {
    const storage = doc.annotationStorage as unknown as { setValue(k: string, v: object): void; remove(k: string): void }
    const keys: string[] = []
    const put = (value: object) => {
      const key = `${STORAGE_PREFIX}${keys.length}`
      keys.push(key)
      storage.setValue(key, { annotationType: TEXT_ANNOTATION, ...value })
    }
    for (const n of notes.value) {
      if (n.ref) {
        if (n.deleted) put({ pageIndex: n.pageIndex, id: n.id, popupRef: n.popupRef, deleted: true })
        else if (n.text !== n.fileText) put({ pageIndex: n.pageIndex, id: n.id, contents: n.text, date: n.date ?? new Date() })
      } else if (!n.deleted) {
        const date = n.date ?? new Date()
        put({ pageIndex: n.pageIndex, rect: n.rect, contents: n.text, color: [255, 204, 0], date })
      }
    }
    return () => { for (const k of keys) storage.remove(k) }
  }

  return {
    notes: visible, dirty, noteMode, setNoteMode, toggleNoteMode, draft, box, card, layoutTick,
    loadAll, noteAtSelection, pin, openEdit, closeBox, deleteNote, noteOf, bubbleEl, stage,
    closeCard: () => { card.value = null },
  }
}

export type PdfNotes = ReturnType<typeof usePdfNotes>
