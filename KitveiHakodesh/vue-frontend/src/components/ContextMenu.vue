<script setup lang="ts">
import { ref, computed, nextTick } from 'vue'
import { IconChevronLeft16Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'

export interface ContextMenuTextItem {
  type?: 'text'
  label: string
  shortcut?: string
  action: () => void
}

export interface ContextMenuSeparatorItem {
  type: 'separator'
}

export interface ContextMenuCheckboxItem {
  type: 'checkbox'
  label: string
  checked: boolean
  onChange: (checked: boolean) => void
}

export interface ContextMenuComponentItem {
  type: 'component'
  component: import('vue').Component
  props?: Record<string, unknown>
}

// Items a submenu may contain — one level of nesting only, so no submenus (and no
// component rows) inside a submenu.
export type ContextMenuLeafItem =
  | ContextMenuTextItem
  | ContextMenuSeparatorItem
  | ContextMenuCheckboxItem

export interface ContextMenuSubmenuItem {
  type: 'submenu'
  label: string
  items: ContextMenuLeafItem[]
}

export type ContextMenuItem =
  | ContextMenuLeafItem
  | ContextMenuComponentItem
  | ContextMenuSubmenuItem

const props = defineProps<{ items: ContextMenuItem[] }>()

const visible = ref(false)
const x = ref(0)
const y = ref(0)
const menuRef = ref<HTMLElement>()
const menuStyle = computed(() => ({ left: `${x.value}px`, top: `${y.value}px` }))

// Saved selection range at the time the menu opened — restored before executing
// any action so that copy operations can read the user's original text selection.
let _savedRange: Range | null = null

function saveSelection(): void {
  const sel = window.getSelection()
  if (sel && sel.rangeCount > 0 && !sel.isCollapsed) {
    _savedRange = sel.getRangeAt(0).cloneRange()
  } else {
    _savedRange = null
  }
}

function restoreSelection(): void {
  if (!_savedRange) return
  const sel = window.getSelection()
  if (!sel) return
  sel.removeAllRanges()
  sel.addRange(_savedRange)
}

useDropdownClose(menuRef, () => {
  visible.value = false
})

// ── Submenu ──────────────────────────────────────────────────────────────────
// One submenu can be open at a time. It opens on hover (and on click/tap, for
// touch — openSubmenu is idempotent so the tap's mouseenter+click pair is safe)
// and closes when the pointer enters any other row or the menu hides.
const openSubmenuIndex = ref<number | null>(null)
// Function ref, NOT a template ref attribute — inside v-for Vue binds `ref="..."`
// as an array, which would silently break the measurement below.
const submenuElement = ref<HTMLElement | null>(null)
function setSubmenuElement(element: unknown): void {
  submenuElement.value = (element as HTMLElement | null) ?? null
}
// RTL menu: the submenu opens toward the physical left (inline-start side).
// `flipped` swaps it to the other side when it would leave the viewport.
const submenuFlipped = ref(false)
// Lifts the submenu panel so its FIRST row's text aligns with the parent row's text.
// The panel has no padding of its own, so this is just the parent row's own top
// padding (see .context-menu-item) negated.
const SUBMENU_TOP_OFFSET = -2
const submenuTopPx = ref(SUBMENU_TOP_OFFSET)
const submenuStyle = computed(() => ({ top: `${submenuTopPx.value}px` }))

function closeSubmenu(): void {
  openSubmenuIndex.value = null
}

async function openSubmenu(index: number): Promise<void> {
  if (openSubmenuIndex.value === index) return
  openSubmenuIndex.value = index
  submenuFlipped.value = false
  submenuTopPx.value = SUBMENU_TOP_OFFSET
  await nextTick()
  if (!submenuElement.value) return
  let rect = submenuElement.value.getBoundingClientRect()
  if (rect.left < 4 || rect.right > window.innerWidth - 4) {
    submenuFlipped.value = true
    await nextTick()
    rect = submenuElement.value.getBoundingClientRect()
  }
  if (rect.bottom > window.innerHeight - 4) {
    submenuTopPx.value = SUBMENU_TOP_OFFSET - (rect.bottom - (window.innerHeight - 4))
  }
}

async function show(event: MouseEvent) {
  event.preventDefault()
  saveSelection()
  await showAtPosition(event.clientX, event.clientY)
}

async function showAtPosition(clientX: number, clientY: number) {
  saveSelection()
  closeSubmenu()
  x.value = clientX
  y.value = clientY
  visible.value = true
  await nextTick()
  if (menuRef.value) {
    const rect = menuRef.value.getBoundingClientRect()
    if (x.value + rect.width > window.innerWidth) x.value = window.innerWidth - rect.width - 4
    if (y.value + rect.height > window.innerHeight) y.value = window.innerHeight - rect.height - 4
  }
}

function hide() {
  closeSubmenu()
  visible.value = false
}

function runItem(item: ContextMenuTextItem) {
  restoreSelection()
  item.action()
  hide()
}

function toggleCheckbox(item: ContextMenuCheckboxItem) {
  item.onChange(!item.checked)
  // Intentionally does NOT close the menu — checkbox is a persistent toggle
}

defineExpose({ show, showAtPosition, hide })
</script>

<template>
  <Teleport to="body">
    <div v-if="visible" ref="menuRef" class="context-menu" :style="menuStyle" @click.stop @mousedown.prevent>
      <template v-for="(item, index) in items" :key="index">
        <div v-if="item.type === 'separator'" class="context-menu-separator" />
        <component
          :is="item.component"
          v-else-if="item.type === 'component'"
          v-bind="item.props ?? {}"
          @close="hide"
          @mouseenter="closeSubmenu"
        />
        <div
          v-else-if="item.type === 'checkbox'"
          class="context-menu-item context-menu-checkbox"
          @click="toggleCheckbox(item as ContextMenuCheckboxItem)"
          @mouseenter="closeSubmenu"
        >
          <span class="checkbox-mark">{{ (item as ContextMenuCheckboxItem).checked ? '✓' : '' }}</span>
          <span>{{ (item as ContextMenuCheckboxItem).label }}</span>
        </div>
        <div
          v-else-if="item.type === 'submenu'"
          class="context-menu-item context-menu-submenu-parent"
          :class="{ open: openSubmenuIndex === index }"
          @mouseenter="openSubmenu(index)"
          @click="openSubmenu(index)"
        >
          <span class="item-label">{{ (item as ContextMenuSubmenuItem).label }}</span>
          <span class="submenu-chevron"><IconChevronLeft16Regular /></span>
          <div
            v-if="openSubmenuIndex === index"
            :ref="setSubmenuElement"
            class="context-menu submenu-panel"
            :class="{ flipped: submenuFlipped }"
            :style="submenuStyle"
          >
            <template v-for="(child, childIndex) in (item as ContextMenuSubmenuItem).items" :key="childIndex">
              <div v-if="child.type === 'separator'" class="context-menu-separator" />
              <div
                v-else-if="child.type === 'checkbox'"
                class="context-menu-item context-menu-checkbox"
                @click.stop="toggleCheckbox(child as ContextMenuCheckboxItem)"
              >
                <span class="checkbox-mark">{{ (child as ContextMenuCheckboxItem).checked ? '✓' : '' }}</span>
                <span>{{ (child as ContextMenuCheckboxItem).label }}</span>
              </div>
              <div v-else class="context-menu-item" @click.stop="runItem(child as ContextMenuTextItem)">
                <span class="item-label">{{ (child as ContextMenuTextItem).label }}</span>
                <span v-if="(child as ContextMenuTextItem).shortcut" class="item-shortcut">{{ (child as ContextMenuTextItem).shortcut }}</span>
              </div>
            </template>
          </div>
        </div>
        <div v-else class="context-menu-item" @click="runItem(item as ContextMenuTextItem)" @mouseenter="closeSubmenu">
          <span class="item-label">{{ (item as ContextMenuTextItem).label }}</span>
          <span v-if="(item as ContextMenuTextItem).shortcut" class="item-shortcut">{{ (item as ContextMenuTextItem).shortcut }}</span>
        </div>
      </template>
    </div>
  </Teleport>
</template>

<style scoped>
.context-menu {
  /* The row metrics, published as variables so any row rendered INSIDE this menu can
     consume them — including rows in a `component` item, which this scoped CSS cannot
     select. Change them here only, and read the ROW METRICS note below first. */
  --context-menu-row-height: 31px;
  --context-menu-row-padding-block: 2px;
  --context-menu-row-padding-inline: 12px;

  position: fixed;
  z-index: 9999;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  box-shadow:
    0 2px 8px rgba(0, 0, 0, 0.12),
    0 8px 24px rgba(0, 0, 0, 0.08);
  min-width: 140px;
  direction: rtl;
  border-radius: 4px;
  /* No padding-block, and no overflow:hidden — see the ROW METRICS note below for the
     padding, and note that the submenu panel is positioned outside this box, so
     clipping overflow would erase it. */
}

/* ── ROW METRICS ───────────────────────────────────────────────────────────────
   Two numbers govern every row in the menu, and each fixes a bug that is easy to
   reintroduce by "tidying" them:

   1. The 2px of breathing room around a separator is the ROW's padding, never the
      separator's margin. Margin belongs to no row, so no hover background can paint
      it and the highlight stops short of the rule — a visible dead gap. As row
      padding the highlight covers it and runs flush to the separator, and to the
      menu's top and bottom edges (which is why the menu box has no padding-block and
      no :first-child/:last-child special cases are needed).

   2. (row height + separator height) must be a MULTIPLE OF 4 — here 31 + 1 = 32.
      "Even" is not enough: a 30px pitch alternates subpixel fractions at 1.25x /
      1.75x / 2.25x because 30 x 1.75 = 52.5, and the compositor then rasterizes each
      rule with a different weight, so the separators visibly disagree in thickness.
      At a multiple of 4 every separator lands on the same fraction at every DPI
      measured and they paint identically.

   Any row that participates in the menu's vertical rhythm must use these same
   metrics, including rows inside a `component` item that this scoped CSS cannot
   reach — see BookViewAnnotationMenuRow.vue, which mirrors them.
   ─────────────────────────────────────────────────────────────────────────────── */
.context-menu-separator {
  height: 1px;
  background: var(--border-color);
  margin-block: 0;
}
.context-menu-item {
  /* box-sizing so the padding insets within the height instead of growing the row. */
  padding: var(--context-menu-row-padding-block) var(--context-menu-row-padding-inline);
  box-sizing: border-box;
  height: var(--context-menu-row-height);
  display: flex;
  align-items: center;
  gap: 6px;
  cursor: pointer;
  font-size: 12px;
  line-height: 1;
  white-space: nowrap;
}
.item-label {
  flex: 1;
}
/* `unicode-bidi: plaintext`, NOT `direction: ltr`: the shortcut is a Latin string in
   an RTL row and must render left-to-right, but `direction: ltr` also reverses this
   flex item's own inline axis, so `margin-inline-start` resolves onto the same side
   as the row's `padding-inline-start` and the two stack — measured 22px of left gap
   against 12px on the label side. plaintext gets the glyph order from first-strong
   -character detection while leaving the box in the row's direction, so the margin
   stays between shortcut and label and both edges measure 12px. */
.item-shortcut {
  flex-shrink: 0;
  font-size: 11px;
  color: var(--text-secondary);
  opacity: 0.7;
  unicode-bidi: plaintext;
  margin-inline-start: 10px;
}
.context-menu-item:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.context-menu-item:active {
  background: color-mix(in srgb, var(--text-primary) 13%, transparent);
}
.context-menu-checkbox {
  gap: 6px;
}
.context-menu-submenu-parent {
  position: relative;
}
.context-menu-submenu-parent.open {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.submenu-chevron {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  margin-inline-start: 10px;
}
/* Icon color comes from the global theme.css `svg { color: var(--text-secondary) }`
   pin, which is exactly the muted tone the menu's secondary text uses. */
.submenu-chevron svg {
  width: 12px;
  height: 12px;
}
/* Nested panel — reuses the .context-menu chrome but anchors to its parent row
   instead of the viewport. Declared after .context-menu so position wins. */
.submenu-panel {
  position: absolute;
  inset-inline-start: 100%;
  z-index: 1;
}
.submenu-panel.flipped {
  inset-inline-start: auto;
  inset-inline-end: 100%;
}
.checkbox-mark {
  display: inline-block;
  width: 12px;
  text-align: center;
  font-size: 11px;
  color: var(--accent-color);
  flex-shrink: 0;
}
</style>
