<script setup lang="ts">
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { ref, computed, nextTick } from 'vue'
import { IconChevronLeft16Regular, IconChevronRight16Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'

export interface ContextMenuTextItem {
  type?: 'text'
  label: string
  shortcut?: string
  /** Tooltip: the longer explanation, so the label itself stays short. */
  title?: string
  action: () => void
  /** Shown greyed out; a click does nothing. */
  disabled?: boolean
  /** Optional icon shown before the label. */
  icon?: import('vue').Component
  /** Not offered as a button on the selection toolbar (it stays in the menu). */
  hideInToolbar?: boolean
  /** A step you repeat (zoom): the menu stays open after the action. */
  keepOpen?: boolean
  /** A small button at the row's end (delete, rename); runs on its own, the menu stays open. */
  endAction?: { icon: import('vue').Component; title: string; action: () => void }
}

/** A caption over the rows below it; not clickable. */
export interface ContextMenuHeadingItem {
  type: 'heading'
  label: string
}

export interface ContextMenuSeparatorItem {
  type: 'separator'
}

export interface ContextMenuCheckboxItem {
  type: 'checkbox'
  label: string
  checked: boolean
  /** Shown greyed out; a click does nothing. */
  disabled?: boolean
  /** Tooltip: the longer explanation, so the label itself stays short. */
  title?: string
  onChange: (checked: boolean) => void
  /** Optional icon shown before the label; replaces the tick; a checked item highlights its row. */
  icon?: import('vue').Component
}

export interface ContextMenuComponentItem {
  type: 'component'
  component: import('vue').Component
  props?: Record<string, unknown>
  /**
   * Offered on the selection toolbar as this icon button. With `action` the button runs it
   * directly (the bar only shows with a selection); without, it opens the same component in
   * a small panel (e.g. highlight's colour choices).
   */
  toolbar?: { icon: import('vue').Component; label: string; action?: () => void }
}

// Items a submenu may contain — one level of nesting only, so no submenus (and no
// component rows) inside a submenu.
export type ContextMenuLeafItem =
  | ContextMenuTextItem
  | ContextMenuSeparatorItem
  | ContextMenuHeadingItem
  | ContextMenuCheckboxItem

export interface ContextMenuSubmenuItem {
  type: 'submenu'
  label: string
  /** Optional icon shown before the label. */
  icon?: import('vue').Component
  /** Shown greyed out; it does not open. */
  disabled?: boolean
  /** The submenu is opening: fetch its list here (its items may be rebuilt as the list arrives). */
  onOpen?: () => void
  items: ContextMenuLeafItem[]
}

export type ContextMenuItem =
  | ContextMenuLeafItem
  | ContextMenuComponentItem
  | ContextMenuSubmenuItem

const props = defineProps<{
  items: ContextMenuItem[]
  /**
   * A toolbar's "more" menu: a submenu row opens IN PLACE (the menu shows only its items, under a
   * back row naming it), like pressing that dropdown on the toolbar. Otherwise a side flyout.
   */
  drillIn?: boolean
}>()

const visible = ref(false)
const x = ref(0)
const y = ref(0)
const menuRef = ref<HTMLElement>()
const menuStyle = computed(() => ({ left: `${x.value}px`, top: `${y.value}px` }))

// Saved selection range at the time the menu opened — restored before executing
// any action so that copy operations can read the user's original text selection.
let _savedRange: Range | null = null

const hostWindow = useHostWindow()

function saveSelection(): void {
  const sel = hostWindow.getSelection()
  if (sel && sel.rangeCount > 0 && !sel.isCollapsed) {
    _savedRange = sel.getRangeAt(0).cloneRange()
  } else {
    _savedRange = null
  }
}

function restoreSelection(): void {
  if (!_savedRange) return
  const sel = hostWindow.getSelection()
  if (!sel) return
  sel.removeAllRanges()
  sel.addRange(_savedRange)
}


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
// The submenu is its OWN fixed panel, a sibling of the menu box — never a child of
// it. The menu box scrolls when it is taller than the viewport, and a panel nested
// inside a scroll box is clipped and widens it into a sideways scroll.
// Placement: toward the physical left of the menu (RTL inline-start), or the right
// when the left does not fit; clamped into the viewport when neither side fits.
// Lifts the panel so its FIRST row's text aligns with the parent row's text (the
// parent row's own top padding, negated).
const SUBMENU_TOP_OFFSET = -2
const VIEWPORT_MARGIN = 4
const submenuLeftPx = ref(0)
const submenuTopPx = ref(0)
// Hidden for the one frame between mounting and measuring, so it never flashes at 0,0.
const submenuPlaced = ref(false)
const submenuStyle = computed(() => ({
  left: `${submenuLeftPx.value}px`,
  top: `${submenuTopPx.value}px`,
  visibility: submenuPlaced.value ? ('visible' as const) : ('hidden' as const),
}))

/**
 * Start coordinate of a box of `size` on one axis: after `forwardFrom` (it extends
 * forward), else ending at `backFrom` (it extends back), else clamped into the
 * viewport. `preferBack` tries the back side first.
 */
function placeAlong(forwardFrom: number, backFrom: number, size: number, viewport: number, preferBack = false): number {
  const max = viewport - VIEWPORT_MARGIN - size
  const forward = forwardFrom <= max ? forwardFrom : null
  const back = backFrom - size >= VIEWPORT_MARGIN ? backFrom - size : null
  const chosen = preferBack ? (back ?? forward) : (forward ?? back)
  return chosen ?? Math.max(VIEWPORT_MARGIN, Math.min(preferBack ? backFrom - size : forwardFrom, max))
}

// The submenu opens toward inline-start: the left in Hebrew, the right in English.
function isRtl(): boolean {
  return hostWindow.document.documentElement.dir !== 'ltr'
}

function closeSubmenu(): void {
  openSubmenuIndex.value = null
}

// ── Drill-in (drillIn): one submenu shown in place of the list, with a back row ──
const drilledIndex = ref<number | null>(null)
const drilledItem = computed(() =>
  drilledIndex.value == null ? null : ((props.items[drilledIndex.value] as ContextMenuSubmenuItem | undefined) ?? null))
// Where the menu was asked to open: re-placed from here when its size changes on drilling.
let anchorX = 0
let anchorY = 0
async function refit(): Promise<void> {
  await nextTick()
  if (!menuRef.value) return
  const rect = menuRef.value.getBoundingClientRect()
  x.value = placeAlong(anchorX, anchorX, rect.width, hostWindow.innerWidth)
  y.value = placeAlong(anchorY, anchorY, rect.height, hostWindow.innerHeight)
}
function drillInto(index: number): void {
  const item = props.items[index] as ContextMenuSubmenuItem
  if (item.disabled) return
  item.onOpen?.()
  drilledIndex.value = index
  void refit()
}
function drillBack(): void {
  drilledIndex.value = null
  void refit()
}
function onSubmenuRow(index: number, row: EventTarget | null): void {
  if (props.drillIn) drillInto(index)
  else void openSubmenu(index, row)
}

async function openSubmenu(index: number, row: EventTarget | null): Promise<void> {
  if (openSubmenuIndex.value === index) return
  const item = props.items[index] as ContextMenuSubmenuItem
  if (item.disabled) return
  item.onOpen?.()
  openSubmenuIndex.value = index
  submenuPlaced.value = false
  await nextTick()
  const panel = submenuElement.value
  if (!panel || !menuRef.value || !(row instanceof HTMLElement)) return

  const menuRect = menuRef.value.getBoundingClientRect()
  const rowRect = row.getBoundingClientRect()
  const { width, height } = panel.getBoundingClientRect()
  // RTL: prefer the left of the menu, else its right; neither fits → clamped, overlapping the menu.
  const left = placeAlong(menuRect.right, menuRect.left, width, hostWindow.innerWidth, isRtl())
  // Downward from the row; no room → upward, its bottom on the row's bottom; else clamped.
  const top = placeAlong(rowRect.top + SUBMENU_TOP_OFFSET, rowRect.bottom - SUBMENU_TOP_OFFSET, height, hostWindow.innerHeight)

  submenuLeftPx.value = left
  submenuTopPx.value = top
  submenuPlaced.value = true
}

useDropdownClose(menuRef, () => {
  visible.value = false
  // No viewport fit: showAtPosition places the menu itself (4px margin, CSS max-height). The fit
  // uses an 8px margin, so a menu flipped up to end at its button got capped a few px short and scrolled.
}, { ignore: [submenuElement], fitToViewport: false })

async function show(event: MouseEvent) {
  event.preventDefault()
  saveSelection()
  await showAtPosition(event.clientX, event.clientY)
}

async function showAtPosition(clientX: number, clientY: number) {
  saveSelection()
  closeSubmenu()
  drilledIndex.value = null
  anchorX = clientX
  anchorY = clientY
  x.value = clientX
  y.value = clientY
  visible.value = true
  await nextTick()
  if (menuRef.value) {
    // Full size whenever it fits: open down/right of the pointer, flip up/left when that
    // side lacks room, and only when neither fits clamp into the viewport (the menu
    // scrolls only when it is taller than the whole window).
    const rect = menuRef.value.getBoundingClientRect()
    x.value = placeAlong(clientX, clientX, rect.width, hostWindow.innerWidth)
    y.value = placeAlong(clientY, clientY, rect.height, hostWindow.innerHeight)
  }
}

function hide() {
  closeSubmenu()
  drilledIndex.value = null
  visible.value = false
}

function runItem(item: ContextMenuTextItem) {
  if (item.disabled) return
  restoreSelection()
  item.action()
  if (!item.keepOpen) hide()
}

function toggleCheckbox(item: ContextMenuCheckboxItem) {
  if (item.disabled) return
  item.onChange(!item.checked)
  // Intentionally does NOT close the menu — checkbox is a persistent toggle
}

defineExpose({ show, showAtPosition, hide })
</script>

<template>
  <Teleport :to="hostWindow.document.body">
    <div v-if="visible" ref="menuRef" class="context-menu" :class="{ 'drill-in': drillIn }" :style="menuStyle" @click.stop @mousedown.prevent @scroll="closeSubmenu">
      <!-- Drilled into a submenu: a back row naming it, then its items in place of the list. -->
      <template v-if="drilledItem">
        <!-- Sticky: the way back stays in view however far the list scrolls. -->
        <div class="context-menu-back-header">
          <div class="context-menu-item context-menu-back" role="button" @click="drillBack">
            <IconChevronRight16Regular class="dir-icon item-icon" />
            <span class="item-label">{{ drilledItem.label }}</span>
          </div>
          <div class="context-menu-separator" />
        </div>
        <template v-for="(child, childIndex) in drilledItem.items" :key="childIndex">
          <div v-if="child.type === 'separator'" class="context-menu-separator" />
          <div v-else-if="child.type === 'heading'" class="context-menu-heading">{{ (child as ContextMenuHeadingItem).label }}</div>
          <div
            v-else-if="child.type === 'checkbox'"
            class="context-menu-item context-menu-checkbox"
            :title="(child as ContextMenuCheckboxItem).title"
            :class="{ 'is-selected': (child as ContextMenuCheckboxItem).icon && (child as ContextMenuCheckboxItem).checked, disabled: (child as ContextMenuCheckboxItem).disabled }"
            @click="toggleCheckbox(child as ContextMenuCheckboxItem)"
          >
            <component :is="(child as ContextMenuCheckboxItem).icon" v-if="(child as ContextMenuCheckboxItem).icon" class="item-icon" />
            <span v-else class="checkbox-mark">{{ (child as ContextMenuCheckboxItem).checked ? '✓' : '' }}</span>
            <span class="item-label">{{ (child as ContextMenuCheckboxItem).label }}</span>
          </div>
          <div v-else class="context-menu-item" :class="{ disabled: (child as ContextMenuTextItem).disabled }" :aria-disabled="(child as ContextMenuTextItem).disabled || undefined" :title="(child as ContextMenuTextItem).title" @click="runItem(child as ContextMenuTextItem)">
            <component :is="(child as ContextMenuTextItem).icon" v-if="(child as ContextMenuTextItem).icon" class="item-icon" />
            <span class="item-label">{{ (child as ContextMenuTextItem).label }}</span>
            <span v-if="(child as ContextMenuTextItem).shortcut" class="item-shortcut">{{ (child as ContextMenuTextItem).shortcut }}</span>
            <button v-if="(child as ContextMenuTextItem).endAction" type="button" class="item-end-action" :title="(child as ContextMenuTextItem).endAction!.title" @click.stop="(child as ContextMenuTextItem).endAction!.action()">
              <component :is="(child as ContextMenuTextItem).endAction!.icon" />
            </button>
          </div>
        </template>
      </template>
      <template v-for="(item, index) in items" v-else :key="index">
        <div v-if="item.type === 'separator'" class="context-menu-separator" />
        <div v-else-if="item.type === 'heading'" class="context-menu-heading">{{ (item as ContextMenuHeadingItem).label }}</div>
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
          :title="(item as ContextMenuCheckboxItem).title"
          :class="{ 'is-selected': (item as ContextMenuCheckboxItem).icon && (item as ContextMenuCheckboxItem).checked, disabled: (item as ContextMenuCheckboxItem).disabled }"
          @click="toggleCheckbox(item as ContextMenuCheckboxItem)"
          @mouseenter="closeSubmenu"
        >
          <!-- An item with an icon shows its state by highlighting the whole row instead of a tick. -->
          <component
            :is="(item as ContextMenuCheckboxItem).icon"
            v-if="(item as ContextMenuCheckboxItem).icon"
            class="item-icon"
          />
          <span v-else class="checkbox-mark">{{ (item as ContextMenuCheckboxItem).checked ? '✓' : '' }}</span>
          <span class="item-label">{{ (item as ContextMenuCheckboxItem).label }}</span>
        </div>
        <div
          v-else-if="item.type === 'submenu'"
          class="context-menu-item context-menu-submenu-parent"
          :class="{ open: openSubmenuIndex === index, disabled: (item as ContextMenuSubmenuItem).disabled }"
          @mouseenter="!drillIn && openSubmenu(index, $event.currentTarget)"
          @click="onSubmenuRow(index, $event.currentTarget)"
        >
          <component :is="(item as ContextMenuSubmenuItem).icon" v-if="(item as ContextMenuSubmenuItem).icon" class="item-icon" />
          <span class="item-label">{{ (item as ContextMenuSubmenuItem).label }}</span>
          <span class="submenu-chevron"><IconChevronLeft16Regular class="dir-icon" /></span>
        </div>
        <div v-else class="context-menu-item" :class="{ disabled: (item as ContextMenuTextItem).disabled }" :aria-disabled="(item as ContextMenuTextItem).disabled || undefined" :title="(item as ContextMenuTextItem).title" @click="runItem(item as ContextMenuTextItem)" @mouseenter="closeSubmenu">
          <component :is="(item as ContextMenuTextItem).icon" v-if="(item as ContextMenuTextItem).icon" class="item-icon" />
          <span class="item-label">{{ (item as ContextMenuTextItem).label }}</span>
          <span v-if="(item as ContextMenuTextItem).shortcut" class="item-shortcut">{{ (item as ContextMenuTextItem).shortcut }}</span>
          <button v-if="(item as ContextMenuTextItem).endAction" type="button" class="item-end-action" :title="(item as ContextMenuTextItem).endAction!.title" @click.stop="(item as ContextMenuTextItem).endAction!.action()">
            <component :is="(item as ContextMenuTextItem).endAction!.icon" />
          </button>
        </div>
      </template>
    </div>
    <template v-if="visible">
      <template v-for="(item, index) in items" :key="index">
        <div
          v-if="openSubmenuIndex === index"
          :ref="setSubmenuElement"
          class="context-menu submenu-panel"
          :style="submenuStyle"
          @click.stop
          @mousedown.prevent
        >
          <template v-for="(child, childIndex) in (item as ContextMenuSubmenuItem).items" :key="childIndex">
            <div v-if="child.type === 'separator'" class="context-menu-separator" />
            <div v-else-if="child.type === 'heading'" class="context-menu-heading">{{ (child as ContextMenuHeadingItem).label }}</div>
            <div
              v-else-if="child.type === 'checkbox'"
              class="context-menu-item context-menu-checkbox"
              :title="(child as ContextMenuCheckboxItem).title"
              :class="{ 'is-selected': (child as ContextMenuCheckboxItem).icon && (child as ContextMenuCheckboxItem).checked, disabled: (child as ContextMenuCheckboxItem).disabled }"
              @click.stop="toggleCheckbox(child as ContextMenuCheckboxItem)"
            >
              <component
                :is="(child as ContextMenuCheckboxItem).icon"
                v-if="(child as ContextMenuCheckboxItem).icon"
                class="item-icon"
              />
              <span v-else class="checkbox-mark">{{ (child as ContextMenuCheckboxItem).checked ? '✓' : '' }}</span>
              <span class="item-label">{{ (child as ContextMenuCheckboxItem).label }}</span>
            </div>
            <div v-else class="context-menu-item" :class="{ disabled: (child as ContextMenuTextItem).disabled }" :title="(child as ContextMenuTextItem).title" @click.stop="runItem(child as ContextMenuTextItem)">
              <component :is="(child as ContextMenuTextItem).icon" v-if="(child as ContextMenuTextItem).icon" class="item-icon" />
              <span class="item-label">{{ (child as ContextMenuTextItem).label }}</span>
              <span v-if="(child as ContextMenuTextItem).shortcut" class="item-shortcut">{{ (child as ContextMenuTextItem).shortcut }}</span>
              <button v-if="(child as ContextMenuTextItem).endAction" type="button" class="item-end-action" :title="(child as ContextMenuTextItem).endAction!.title" @click.stop="(child as ContextMenuTextItem).endAction!.action()">
                <component :is="(child as ContextMenuTextItem).endAction!.icon" />
              </button>
            </div>
          </template>
        </div>
      </template>
    </template>
  </Teleport>
</template>

<style scoped>
.item-icon {
  flex-shrink: 0;
  width: 16px;
  height: 16px;
  color: inherit;
}
/* A checked option with an icon: the icon itself shows the state (accent colour). No tick. */
.context-menu-item.is-selected .item-icon {
  color: var(--accent-color);
}
.context-menu {
  /* The row metrics, published as variables so any row rendered INSIDE this menu can
     consume them — including rows in a `component` item, which this scoped CSS cannot
     select. Change them here only, and read the ROW METRICS note below first. */
  --context-menu-row-height: 27px;
  --context-menu-row-padding-block: 2px;
  --context-menu-row-padding-inline: 10px;

  position: fixed;
  z-index: 9999;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  box-shadow:
    0 2px 8px rgba(0, 0, 0, 0.12),
    0 8px 24px rgba(0, 0, 0, 0.08);
  min-width: 140px;
  border-radius: 4px;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  /* No padding-block — see the ROW METRICS note below. Scrolls when taller than the
     viewport; the submenu is a sibling panel, so it is not clipped by this. */
  max-height: calc(100vh - 8px);
  overflow-y: auto;
  overflow-x: hidden;
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

   2. (row height + separator height) must be a MULTIPLE OF 4 — here 27 + 1 = 28.
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
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
}
/* A caption over a group of rows (a section of a list). */
.context-menu-heading {
  padding: 4px var(--context-menu-row-padding-inline) 0;
  font-size: 10px;
  color: var(--text-secondary);
  cursor: default;
}
/* A small button at a row's end (delete, rename). */
.item-end-action {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  padding: 0;
  border-radius: 3px;
  opacity: 0.7;
}
.item-end-action:hover {
  opacity: 1;
  background: color-mix(in srgb, var(--text-primary) 12%, transparent);
}
.item-end-action svg {
  width: 14px;
  height: 14px;
}
/* `unicode-bidi: plaintext`, NOT `direction: ltr`: the shortcut is a Latin string in
   an RTL row and must render left-to-right, but `direction: ltr` also reverses this
   flex item's own inline axis, so `margin-inline-start` resolves onto the same side
   as the row's `padding-inline-start` and the two stack — measured 22px of left gap
   against the padding on the label side. plaintext gets the glyph order from first-strong
   -character detection while leaving the box in the row's direction, so the margin
   stays between shortcut and label and both edges match the row padding. */
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
.context-menu-item.disabled {
  opacity: 0.45;
  cursor: default;
  background: none;
}
.context-menu-checkbox {
  gap: 6px;
}
.context-menu-back {
  font-weight: 600;
}
.context-menu-back-header {
  position: sticky;
  top: 0;
  z-index: 1;
  background: var(--bg-secondary);
}
/* A toolbar's "more" menu (drillIn), list and drilled submenu alike: at most two thirds of the window. */
.context-menu.drill-in {
  max-height: calc(100vh * 2 / 3);
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
/* Submenu panel — the .context-menu chrome, placed by openSubmenu() beside its row. */
.submenu-panel {
  z-index: 10000;
  /* A long list (related books) scrolls; a long title is cut with an ellipsis. */
  max-width: 360px;
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
