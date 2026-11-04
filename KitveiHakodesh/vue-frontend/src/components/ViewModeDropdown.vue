<script setup lang="ts">
import { computed, h, ref, type Component } from 'vue'
import {
  IconTextBulletListRtl20Regular,
  IconGrid20Regular,
  IconGridKanban20Regular,
  IconTextBulletListTree20Regular,
} from '@iconify-prerendered/vue-fluent'
import ContextMenu, { type ContextMenuItem } from '@/components/ContextMenu.vue'
import type { BooksView } from '@/stores/settingsStore'

/** Every mode any page can offer; a page offers the subset it passes labels for. */
type ViewMode = BooksView

// Labels come from the caller, not from here: each page keeps its own wording and
// locale convention (the catalog's strings are Hebrew in both locales, home's are not).
// The label keys are also the menu: a page without a tree view simply passes no
// tree label, so home's three-mode menu and the catalog's four share this control
// without sharing a setting.
const props = defineProps<{
  labels: Partial<Record<ViewMode, string>>
  /** Tooltip of the button. */
  title: string
}>()
// Two roots (button + menu), so attrs such as a sizing class go to the button.
defineOptions({ inheritAttrs: false })
const mode = defineModel<ViewMode>({ required: true })

// The list icon is drawn right-to-left; .dir-icon mirrors it for an LTR UI.
const ListViewIcon = () => h(IconTextBulletListRtl20Regular, { class: 'dir-icon' })
// The tree icon is drawn left-to-right; .dir-icon-ltr mirrors it for a Hebrew UI.
const TreeViewIcon = () => h(IconTextBulletListTree20Regular, { class: 'dir-icon-ltr' })

const ICONS: Record<ViewMode, Component> = {
  list: ListViewIcon,
  cards: IconGridKanban20Regular,
  tiles: IconGrid20Regular,
  tree: TreeViewIcon,
}

// Several views, so the button opens a menu of them. It shows the view you are IN;
// the menu ticks it.
const menuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
const menuItems = computed<ContextMenuItem[]>(() =>
  (Object.keys(props.labels) as ViewMode[]).map((view) => ({
    type: 'checkbox',
    label: props.labels[view]!,
    icon: ICONS[view],
    checked: mode.value === view,
    onChange: () => {
      mode.value = view
      menuRef.value?.hide()
    },
  })),
)

function openMenu(event: MouseEvent) {
  const rect = (event.currentTarget as HTMLElement).getBoundingClientRect()
  void menuRef.value?.showAtPosition(rect.left, rect.bottom)
}
</script>

<template>
  <!-- mousedown.prevent: the catalog's search field must keep focus (its whole
       keyboard model rests on it), and blur would otherwise fire before click. -->
  <button v-bind="$attrs" class="bar-btn" :title="title" @mousedown.prevent @click="openMenu">
    <component :is="ICONS[mode]" :key="mode" />
  </button>
  <ContextMenu ref="menuRef" :items="menuItems" />
</template>
