<script setup lang="ts">
import { computed, h, ref, type Component } from 'vue'
import {
  IconTextBulletListRtl20Regular,
  IconGrid20Regular,
  IconGridKanban20Regular,
} from '@iconify-prerendered/vue-fluent'
import ContextMenu, { type ContextMenuItem } from '@/components/ContextMenu.vue'
import type { CardsViewMode } from '@/stores/settingsStore'

// Labels come from the caller, not from here: each page keeps its own wording and
// locale convention (the catalog's strings are Hebrew in both locales, home's are not).
const props = defineProps<{
  labels: Record<CardsViewMode, string>
  /** Tooltip of the button. */
  title: string
}>()
// Two roots (button + menu), so attrs such as a sizing class go to the button.
defineOptions({ inheritAttrs: false })
const mode = defineModel<CardsViewMode>({ required: true })

// The list icon is drawn right-to-left; .dir-icon mirrors it for an LTR UI.
const ListViewIcon = () => h(IconTextBulletListRtl20Regular, { class: 'dir-icon' })

const ICONS: Record<CardsViewMode, Component> = {
  list: ListViewIcon,
  cards: IconGridKanban20Regular,
  tiles: IconGrid20Regular,
}

// Three views, so the button opens a menu of them. It shows the view you are IN;
// the menu ticks it.
const menuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
const menuItems = computed<ContextMenuItem[]>(() =>
  (Object.keys(ICONS) as CardsViewMode[]).map((view) => ({
    type: 'checkbox',
    label: props.labels[view],
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
