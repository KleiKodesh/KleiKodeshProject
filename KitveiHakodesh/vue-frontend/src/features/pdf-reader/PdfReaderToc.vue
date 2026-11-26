<script setup lang="ts">
import { computed, nextTick, ref, type Directive } from 'vue'
import { useI18n } from 'vue-i18n'
import { useEventListener } from '@vueuse/core'
import { IconAdd16Regular, IconMoreHorizontal16Regular } from '@iconify-prerendered/vue-fluent'
import TocTreeLayout from '@/components/TocTreeLayout.vue'
import TocTreeSection from '@/components/TocTreeSection.vue'
import TreeNode from '@/components/TreeNode.vue'
import ContextMenu, { type ContextMenuItem } from '@/components/ContextMenu.vue'
import type { TreeNodeItem } from '@/components/treeTypes'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { SegmentSearchTree } from '@/utils/segmentSearchTree'
import type { PdfOutlineNode } from './usePdfOutline'
import type { OutlineDropPosition, PdfOutlineEdit } from './usePdfOutlineEdit'

/**
 * The PDF viewer's table of contents: the book view's TOC panel (shared TocTreeLayout +
 * TocTreeSection, as FastDocViewToc) fed the PDF's outline, with the old viewer's direct
 * outline editing (no edit mode; rows always navigate on click):
 * - add button beside the search box (Ctrl+B): an entry at the current page, renamed at once
 * - right-click or the row's ... button: rename (F2), retarget to the current page,
 *   add after, add child, delete (Del)
 * - double-click renames (Enter / leaving the box commits, Esc cancels)
 * - drag a row: top quarter = before, bottom quarter = after, middle = into
 * - Alt+Up/Down move among siblings, Alt+Left/Right nest deeper / out (RTL, like Left expands)
 */
const props = defineProps<{ outline: PdfOutlineNode[]; activeNodeId?: number; edit?: PdfOutlineEdit }>()
const emit = defineEmits<{ select: [node: PdfOutlineNode] }>()

const { t } = useI18n()
const hostWindow = useHostWindow()

const query = ref('')
const sectionRef = ref<InstanceType<typeof TocTreeSection> | null>(null)
const searchTree = computed(() => new SegmentSearchTree(props.outline))
const editable = computed(() => !!props.edit?.canEdit.value)
/** Once edited, new rows are the same tree changed in place (the reader's position stays). */
const inPlace = computed(() => !!props.edit?.dirty.value)

function focusList() {
  sectionRef.value?.containerRef?.()?.focus()
}
function onSelect(node: TreeNodeItem) {
  emit('select', node as PdfOutlineNode)
}

// ── Rename ──────────────────────────────────────────────────────────────────

const renamingId = ref<number | null>(null)
const renameText = ref('')
/** A real pointer press outside the box since renaming began: only then may a blur commit. */
let pressedOutside = false

const vFocusSelect: Directive<HTMLInputElement> = {
  mounted: (el) => {
    el.focus({ preventScroll: true })
    el.select()
  },
}

async function startRename(id: number) {
  if (!editable.value) return
  if (query.value) query.value = ''
  const node = props.outline.find((n) => n.id === id)
  if (!node) return
  renameText.value = node.text
  renamingId.value = id
  pressedOutside = false
  await nextTick()
  sectionRef.value?.revealNode(id)
}

function commitRename() {
  const id = renamingId.value
  if (id == null) return
  renamingId.value = null
  props.edit?.rename(id, renameText.value)
}

function cancelRename() {
  renamingId.value = null
  focusList()
}

function onRenameBlur(event: FocusEvent) {
  // The first click of the double-click navigated; PDF.js focuses the target page's text
  // layer once it draws. That late focus steal must not end the rename (pdf-viewers bug
  // list): only a real press elsewhere, or Enter / Esc, ends it.
  const input = event.target as HTMLInputElement
  if (!pressedOutside && renamingId.value != null && input.isConnected) {
    input.focus({ preventScroll: true })
    return
  }
  commitRename()
}

useEventListener(hostWindow, 'pointerdown', (event: PointerEvent) => {
  if (renamingId.value == null) return
  if (!(event.target as Element | null)?.closest?.('.outline-rename')) pressedOutside = true
}, { capture: true })

function onRenameKey(event: KeyboardEvent) {
  event.stopPropagation()
  if (event.key === 'Enter') {
    event.preventDefault()
    commitRename()
    focusList()
  } else if (event.key === 'Escape') {
    event.preventDefault()
    cancelRename()
  }
}

// ── Actions ─────────────────────────────────────────────────────────────────

/** The row an action applies to: the keyboard ring's row, else the current entry. */
function targetId(): number | undefined {
  return sectionRef.value?.focusedNode()?.id ?? props.activeNodeId
}

async function afterAdd(id: number | null, parentId?: number) {
  if (id == null) return
  await nextTick()
  if (parentId != null) sectionRef.value?.expandNode(parentId)
  await startRename(id)
}

function addEntry(afterId = targetId()) {
  if (!props.edit) return
  query.value = ''
  void afterAdd(props.edit.addAfter(afterId, t('pdfReader.outline.newEntry')))
}

function addChildEntry(parentId: number) {
  if (!props.edit) return
  void afterAdd(props.edit.addChild(parentId, t('pdfReader.outline.newEntry')), parentId)
}

const menuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
const menuItems = ref<ContextMenuItem[]>([])

function openRowMenu(node: TreeNodeItem, x: number, y: number) {
  if (!editable.value || !props.edit) return
  const edit = props.edit
  menuItems.value = [
    { label: t('pdfReader.outline.rename'), shortcut: 'F2', action: () => void startRename(node.id) },
    { label: t('pdfReader.outline.retarget'), action: () => edit.retarget(node.id) },
    { type: 'separator' },
    { label: t('pdfReader.outline.addAfter'), shortcut: 'Ctrl+B', action: () => addEntry(node.id) },
    { label: t('pdfReader.outline.addChild'), action: () => addChildEntry(node.id) },
    { type: 'separator' },
    { label: t('pdfReader.outline.delete'), shortcut: 'Del', action: () => edit.remove(node.id) },
  ]
  void menuRef.value?.showAtPosition(x, y)
}

function onRowContextMenu(event: MouseEvent, node: TreeNodeItem) {
  if (!editable.value) return
  event.preventDefault()
  event.stopPropagation()
  openRowMenu(node, event.clientX, event.clientY)
}

function onRowMenuButton(event: MouseEvent, node: TreeNodeItem) {
  const rect = (event.currentTarget as HTMLElement).getBoundingClientRect()
  openRowMenu(node, rect.left, rect.bottom)
}

// ── Keyboard (from the search box or the list) ─────────────────────────────

function onKeydown(event: KeyboardEvent) {
  if (!editable.value || !props.edit || renamingId.value != null) return
  const edit = props.edit
  const inSearch = (event.target as Element | null)?.tagName === 'INPUT'
  const ctrl = event.ctrlKey || event.metaKey
  if (ctrl && !event.altKey && !event.shiftKey && event.code === 'KeyB') {
    event.preventDefault()
    addEntry()
    return
  }
  const id = targetId()
  if (id == null) return
  let handled = true
  if (event.key === 'F2' && !ctrl && !event.altKey) void startRename(id)
  else if (event.key === 'Delete' && !ctrl && !event.altKey && !(inSearch && query.value)) edit.remove(id)
  else if (event.altKey && !ctrl && !query.value && event.key === 'ArrowUp') edit.moveSibling(id, -1)
  else if (event.altKey && !ctrl && !query.value && event.key === 'ArrowDown') edit.moveSibling(id, 1)
  else if (event.altKey && !ctrl && !query.value && (event.key === 'ArrowLeft' || event.key === 'ArrowRight')) {
    // RTL: Alt+Left nests deeper, matching plain Left's expand direction.
    const deeper = (event.key === 'ArrowLeft') === (hostWindow.getComputedStyle(event.currentTarget as Element).direction === 'rtl')
    if (deeper) {
      const parentId = edit.indent(id)
      if (parentId != null) void nextTick(() => sectionRef.value?.expandNode(parentId))
    } else edit.outdent(id)
  } else handled = false
  if (handled) {
    event.preventDefault()
    event.stopPropagation()
  }
}

// ── Drag & drop ─────────────────────────────────────────────────────────────

const DRAG_THRESHOLD = 5
const EDGE = 24
let press: { id: number; x: number; y: number } | null = null
const draggingId = ref<number | null>(null)
const drop = ref<{ id: number; position: OutlineDropPosition } | null>(null)
const indicator = ref<{ top: number; left: number; width: number } | null>(null)

function onRowPointerDown(event: PointerEvent, node: TreeNodeItem) {
  if (!editable.value || event.button !== 0 || renamingId.value != null || query.value) return
  press = { id: node.id, x: event.clientX, y: event.clientY }
}

function rowAt(x: number, y: number): { el: HTMLElement; id: number } | null {
  const el = hostWindow.document.elementFromPoint(x, y)?.closest<HTMLElement>('[data-outline-id]')
  return el ? { el, id: Number(el.dataset.outlineId) } : null
}

useEventListener(hostWindow, 'pointermove', (event: PointerEvent) => {
  if (!press) return
  if (draggingId.value == null) {
    if (Math.hypot(event.clientX - press.x, event.clientY - press.y) < DRAG_THRESHOLD) return
    draggingId.value = press.id
  }
  const list = sectionRef.value?.containerRef?.()
  if (list) {
    const box = list.getBoundingClientRect()
    if (event.clientY < box.top + EDGE) list.scrollBy(0, -8)
    else if (event.clientY > box.bottom - EDGE) list.scrollBy(0, 8)
  }
  const row = rowAt(event.clientX, event.clientY)
  if (!row || !props.edit?.canDrop(draggingId.value, row.id)) {
    drop.value = null
    indicator.value = null
    return
  }
  const rect = row.el.getBoundingClientRect()
  const offset = (event.clientY - rect.top) / rect.height
  const position: OutlineDropPosition = offset < 0.25 ? 'before' : offset > 0.75 ? 'after' : 'into'
  drop.value = { id: row.id, position }
  indicator.value = position === 'into' ? null
    : { top: position === 'before' ? rect.top : rect.bottom, left: rect.left, width: rect.width }
})

function endDrag(commit: boolean) {
  const wasDragging = draggingId.value != null
  if (commit && wasDragging && drop.value && props.edit) {
    const { id, position } = drop.value
    if (props.edit.moveTo(draggingId.value!, id, position) && position === 'into') {
      void nextTick(() => sectionRef.value?.expandNode(id))
    }
  }
  press = null
  draggingId.value = null
  drop.value = null
  indicator.value = null
  if (wasDragging) {
    // Swallow the click that follows this pointerup (it would navigate). Removed on a
    // zero-timeout rather than { once: true }, which would linger and eat the next unrelated
    // click when no click follows (pointer released outside the window).
    const swallow = (e: MouseEvent) => {
      e.stopPropagation()
      e.preventDefault()
    }
    hostWindow.addEventListener('click', swallow, true)
    hostWindow.setTimeout(() => hostWindow.removeEventListener('click', swallow, true), 0)
  }
}
useEventListener(hostWindow, 'pointerup', () => endDrag(true))
useEventListener(hostWindow, 'pointercancel', () => endDrag(false))
useEventListener(hostWindow, 'keydown', (event: KeyboardEvent) => {
  if (event.key === 'Escape' && draggingId.value != null) endDrag(false)
})
</script>

<template>
  <TocTreeLayout v-model:query="query" :data-ctrlb-enabled="editable && edit ? '' : undefined" @focus-list="focusList" @keydown="onKeydown">
    <TocTreeSection
      ref="sectionRef"
      class="pdf-reader-toc-section"
      :class="{ 'is-dragging': draggingId != null }"
      :title="null"
      :entries="outline"
      :filter="query"
      :active-entry-id="activeNodeId"
      :search-tree="searchTree"
      :nodes-update-in-place="inPlace"
      @select="onSelect"
    >
      <!-- Editable rows: only while not searching (search results keep the plain rows). -->
      <template v-if="editable && !query" #row="{ node, expanded, focused, header, toggle, select }">
        <div
          class="outline-row"
          :class="{
            'is-drop-into': drop?.id === node.id && drop.position === 'into',
            'is-drag-source': draggingId === node.id,
          }"
          :data-outline-id="header ? undefined : node.id"
          @pointerdown="!header && onRowPointerDown($event, node)"
          @contextmenu="onRowContextMenu($event, node)"
        >
          <TreeNode
            :node="node"
            :expanded="expanded"
            :active="node.id === activeNodeId"
            :focused="focused"
            :filtered="false"
            @toggle="toggle()"
            @select="select($event)"
            @dblclick="!header && startRename(node.id)"
          >
            <input
              v-if="renamingId === node.id && !header"
              v-model="renameText"
              v-focus-select
              class="outline-rename"
              type="text"
              @click.stop
              @dblclick.stop
              @pointerdown.stop
              @keydown="onRenameKey"
              @blur="onRenameBlur"
            />
            <template v-else>{{ node.text }}</template>
          </TreeNode>
          <button
            v-if="!header && renamingId !== node.id"
            type="button"
            class="outline-row-menu"
            :title="$t('pdfReader.outline.rowMenu')"
            :aria-label="$t('pdfReader.outline.rowMenu')"
            @pointerdown.stop
            @click.stop="onRowMenuButton($event, node)"
          >
            <IconMoreHorizontal16Regular />
          </button>
        </div>
      </template>
    </TocTreeSection>
    <template v-if="editable" #actions>
      <button
        type="button"
        class="outline-add"
        :title="$t('pdfReader.outline.add')"
        :aria-label="$t('pdfReader.outline.add')"
        @click="addEntry()"
      >
        <IconAdd16Regular />
      </button>
    </template>
  </TocTreeLayout>
  <div
    v-if="indicator"
    class="outline-drop-line"
    :style="{ top: `${indicator.top - 1}px`, left: `${indicator.left}px`, width: `${indicator.width}px` }"
  />
  <ContextMenu ref="menuRef" :items="menuItems" />
</template>

<style scoped>
.pdf-reader-toc-section {
  flex: 1;
  min-height: 0;
}
.is-dragging {
  cursor: grabbing;
  user-select: none;
}
.outline-row {
  position: relative;
}
.outline-row.is-drag-source {
  opacity: 0.5;
}
.outline-row.is-drop-into :deep(.tree-row) {
  background: color-mix(in srgb, var(--accent-color, #3478f6) 18%, transparent);
}
.outline-row-menu {
  position: absolute;
  inset-inline-end: 0;
  top: 0;
  bottom: 0;
  width: 24px;
  display: none;
  align-items: center;
  justify-content: center;
  padding: 0;
  border: none;
  background: var(--tree-bg, var(--bg-primary));
  color: var(--text-secondary);
  cursor: pointer;
}
.outline-row:hover .outline-row-menu {
  display: flex;
}
.is-dragging .outline-row-menu {
  display: none !important;
}
.outline-row-menu:hover {
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 10%, var(--tree-bg, var(--bg-primary)));
}
.outline-rename {
  width: 100%;
  box-sizing: border-box;
  font: inherit;
  color: var(--text-primary);
  background: var(--bg-primary);
  border: 1px solid var(--accent-color, #3478f6);
  border-radius: 3px;
  padding: 0 4px;
  line-height: calc(var(--rh, 28px) - 6px);
  outline: none;
}
.outline-add {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  /* Negative block margin: the button must not make the search row taller. */
  margin-block: -6px;
  margin-inline-start: 4px;
  padding: 0;
  border: none;
  border-radius: 50%;
  background: none;
  color: var(--text-secondary);
  cursor: pointer;
}
.outline-add:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
  color: var(--text-primary);
}
.outline-drop-line {
  position: fixed;
  height: 2px;
  background: var(--accent-color, #3478f6);
  pointer-events: none;
  z-index: 10;
}
</style>
