import { ref, type Component, type Ref } from 'vue'
import type ContextMenu from '@/components/ContextMenu.vue'
import type { ContextMenuLeafItem, ContextMenuSubmenuItem } from '@/components/ContextMenu.vue'

/**
 * The "more" menu of a reading toolbar (book view, FastDocView, a future viewer), as the PDF
 * toolbar's: ONE menu surface (ContextMenu), items built from data. A dropdown control that
 * collapsed becomes a row with a submenu arrow; picking an item applies it and closes the menu.
 *
 * Usage: `const more = useToolbarMoreMenu(toolbarPosition)`, `<ContextMenu :ref="more.menuRef" :items="..." />`,
 * the "more" button calls `more.openFrom(buttonEl)`, an item that should close the menu calls `more.hide()`.
 */
export function useToolbarMoreMenu(toolbarPosition: Ref<'top' | 'bottom' | 'left' | 'right'>) {
  const menuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
  const isOpen = ref(false)

  /** Opens beside the toolbar, never over it: below a top bar, above a bottom bar, aside a side bar. */
  function openFrom(button: HTMLElement | null) {
    const rect = button?.getBoundingClientRect()
    if (!rect) return
    const position = toolbarPosition.value
    const x = position === 'left' ? rect.right : rect.left
    const y = position === 'bottom' ? rect.top : rect.bottom
    isOpen.value = true
    void menuRef.value?.showAtPosition(x, y)
  }
  function hide() {
    isOpen.value = false
    menuRef.value?.hide()
  }
  return { menuRef, openFrom, hide }
}

export interface MenuChoice<V> {
  value: V
  label: string
  icon?: Component
  disabled?: boolean
  title?: string
}

/**
 * The choices of ONE setting as ticked rows (the current one ticked). Picking applies it and,
 * unless `keepOpen`, closes the whole menu - the rule of every "more" submenu.
 */
export function choiceItems<V>(
  choices: readonly MenuChoice<V>[],
  current: V | null,
  pick: (value: V) => void,
  closeMenu: () => void,
): ContextMenuLeafItem[] {
  return choices.map((c) => ({
    type: 'checkbox' as const,
    label: c.label,
    icon: c.icon,
    title: c.title,
    disabled: c.disabled,
    checked: c.value === current,
    onChange: () => {
      pick(c.value)
      closeMenu()
    },
  }))
}

/** A toolbar dropdown's row in "more": its label (and icon), a submenu arrow, the items. */
export function submenuRow(
  label: string,
  icon: Component | undefined,
  items: ContextMenuLeafItem[],
  options: { onOpen?: () => void; disabled?: boolean } = {},
): ContextMenuSubmenuItem {
  return { type: 'submenu', label, icon, items, onOpen: options.onOpen, disabled: options.disabled }
}
