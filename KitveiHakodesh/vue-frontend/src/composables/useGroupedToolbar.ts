import { computed, type ComputedRef, type Ref } from 'vue'
import { useToolbarOverflow } from './useToolbarOverflow'

/**
 * A reading toolbar laid out in groups and zones, with a "more" menu for what does not fit
 * (the PDF toolbar's layout, shared by the book view and next FastDocView).
 *
 * - zone: where the group sits - start / center / end (`.tb-zone-*` in readingToolbar.css).
 * - group: related controls; the "more" menu puts a separator between rows of different groups.
 * - overflow order: a separate list, first key = first to collapse (what a reader needs least).
 * - width: the room a control takes, in button-widths (a pair = 2); a function for a measured box.
 *
 * The items list is in the toolbar's own order; the menu lists collapsed controls in that order too.
 */
export type ToolbarZone = 'start' | 'center' | 'end'
export interface GroupedToolbarItem<Key extends string> {
  key: Key
  group: string
  zone: ToolbarZone
  /** Button-widths; default 1. */
  width?: number | (() => number)
}

/** Keys (in toolbar order) whose row starts a new group: a separator goes before each. */
export function groupStartsOf<Key extends string>(
  keysInToolbarOrder: readonly Key[],
  groupOf: (key: Key) => string,
): Set<Key> {
  const starts = new Set<Key>()
  keysInToolbarOrder.forEach((k, i) => {
    if (i > 0 && groupOf(k) !== groupOf(keysInToolbarOrder[i - 1]!)) starts.add(k)
  })
  return starts
}

/** Item width in button-widths. */
export function itemWidth<Key extends string>(item: GroupedToolbarItem<Key> | undefined): number {
  const w = item?.width ?? 1
  return typeof w === 'function' ? w() : w
}

export function useGroupedToolbar<Key extends string>(
  toolbarEl: Ref<HTMLElement | null>,
  /** Every control, in toolbar order. */
  items: readonly GroupedToolbarItem<Key>[],
  /** Collapse order: first key = first to go into "more". */
  overflowOrder: readonly Key[],
  /** Whether a control is rendered now (a control not drawn takes no room). */
  isPresent: (key: Key) => boolean,
  isVertical: ComputedRef<boolean>,
) {
  const byKey = new Map(items.map((it) => [it.key, it]))
  const order = items.map((it) => it.key)
  const groupOf = (key: Key) => byKey.get(key)?.group ?? ''

  const presentKeys = computed(() => overflowOrder.filter(isPresent))
  const { hasToolbarOverflow, overflowedKeys, toolbarButtonVisible } = useToolbarOverflow(
    toolbarEl,
    presentKeys,
    isVertical,
    (key: Key) => itemWidth(byKey.get(key)),
  )

  /** The collapsed controls in toolbar order (the "more" menu's rows). */
  const overflowedInToolbarOrder = computed(() =>
    [...overflowedKeys.value].sort((a, b) => order.indexOf(a) - order.indexOf(b)),
  )
  /** Collapsed controls that start a new group in the menu. */
  const overflowGroupStarts = computed(() => groupStartsOf(overflowedInToolbarOrder.value, groupOf))

  function isOverflowed(key: Key) {
    return overflowedKeys.value.includes(key)
  }

  return {
    hasToolbarOverflow,
    overflowedKeys,
    overflowedInToolbarOrder,
    overflowGroupStarts,
    toolbarButtonVisible,
    isOverflowed,
    groupOf,
  }
}
