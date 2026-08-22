<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue'

import { useDropdownClose } from '@/composables/useDropdownClose'
import { useListKeys } from '@/composables/useListKeyNav'
import {
  IconOpen28Regular,
  IconChevronDoubleLeft20Regular,
} from '@iconify-prerendered/vue-fluent'
import { APP_NAV_ITEMS, APP_NAV_SETTINGS_ITEM } from './appNavItems'
import { useSettingsStore } from '@/stores/settingsStore'
import { useAppNavigation } from '@/composables/useAppNavigation'
import { showPopOutButton } from '@/webview-host/bridge'
import { togglePopOut } from '@/webview-host/bridge'
import { useEventListener } from '@vueuse/core'

const emit = defineEmits<{ close: [] }>()

const props = defineProps<{ toggleButtonEl?: HTMLElement | null }>()

const { navigateInNewTab } = useAppNavigation()

const menuRef = ref<HTMLElement | null>(null)
useDropdownClose(menuRef, () => emit('close'), {
  toggleButton: computed(() => props.toggleButtonEl ?? null),
})

const settingsStore = useSettingsStore()

// The rows below the divider, in render order. Pop-out is hosted-only, so the keyboard
// indexes are read off this list rather than counted by hand.
//
// The sidebar row only ever offers to OPEN the sidebar: while the sidebar is up this menu
// cannot be reached at all (AppTitleBar drops the hamburger and Ctrl+M), and closing it
// again is the rail's own bottom button.
type MenuRow = 'settings' | 'navSidebar' | 'popOut'

const menuRows: MenuRow[] = ['settings', 'navSidebar', ...(showPopOutButton ? ['popOut' as const] : [])]

function menuRowIndex(row: MenuRow) {
  return APP_NAV_ITEMS.length + menuRows.indexOf(row)
}

const itemCount = APP_NAV_ITEMS.length + menuRows.length

const { focusedIndex } = useListKeys(menuRef, () => itemCount, (index) => {
  activateIndex(index)
})

function activateIndex(index: number) {
  if (index < APP_NAV_ITEMS.length) {
    onTap(APP_NAV_ITEMS[index]!.label)
    return
  }
  const row = menuRows[index - APP_NAV_ITEMS.length]
  if (row === 'settings') onTap(APP_NAV_SETTINGS_ITEM.label)
  else if (row === 'navSidebar') onShowNavSidebar()
  else if (row === 'popOut') onPopOut()
}

// Close on Escape
useEventListener(menuRef, 'keydown', (e: KeyboardEvent) => {
  if (e.code === 'Escape') {
    e.preventDefault()
    e.stopPropagation()
    emit('close')
  }
})

// Focus the dropdown on mount so keyboard nav works immediately
onMounted(() => nextTick(() => menuRef.value?.focus()))

async function onTap(label: string) {
  await navigateInNewTab(label)
  emit('close')
}

function onShowNavSidebar() {
  settingsStore.navSidebarVisible = true
  emit('close')
}

function onPopOut() {
  togglePopOut()
  emit('close')
}
</script>

<template>
  <div ref="menuRef" class="nav-dropdown" tabindex="0" @click.stop>
    <button
      v-for="(item, index) in APP_NAV_ITEMS"
      :key="item.label"
      class="nav-row"
      :class="{ 'nav-row--focused': focusedIndex === index }"
      data-nav-item
      :title="`${item.label} (${item.shortcut})`"
      @click="onTap(item.label)"
    >
      <span class="nav-icon">
        <component :is="item.icon" :style="item.color ? { color: item.color } : {}" />
      </span>
      <span class="nav-label">{{ item.label }}</span>
      <span class="nav-shortcut">{{ item.shortcut }}</span>
    </button>
    <hr class="nav-divider" />
    <button
      class="nav-row"
      :class="{ 'nav-row--focused': focusedIndex === menuRowIndex('settings') }"
      data-nav-item
      :title="`${APP_NAV_SETTINGS_ITEM.label} (${APP_NAV_SETTINGS_ITEM.shortcut})`"
      @click="onTap(APP_NAV_SETTINGS_ITEM.label)"
    >
      <span class="nav-icon"><component :is="APP_NAV_SETTINGS_ITEM.icon" /></span>
      <span class="nav-label">{{ APP_NAV_SETTINGS_ITEM.label }}</span>
      <span class="nav-shortcut">{{ APP_NAV_SETTINGS_ITEM.shortcut }}</span>
    </button>
    <button
      class="nav-row"
      :class="{ 'nav-row--focused': focusedIndex === menuRowIndex('navSidebar') }"
      data-nav-item
      title="הצג סרגל צד"
      @click="onShowNavSidebar"
    >
      <span class="nav-icon"><IconChevronDoubleLeft20Regular /></span>
      <span class="nav-label">סרגל צד</span>
    </button>
    <button
      v-if="showPopOutButton"
      class="nav-row"
      :class="{ 'nav-row--focused': focusedIndex === menuRowIndex('popOut') }"
      data-nav-item
      title="פתח בחלון עצמאי או החזר לחלונית"
      @click="onPopOut"
    >
      <span class="nav-icon"><IconOpen28Regular /></span>
      <span class="nav-label">חלון עצמאי / חלונית</span>
    </button>
  </div>
</template>

<style scoped>
.nav-dropdown {
  position: absolute;
  top: calc(100% + 3px);
  right: 0;
  z-index: 200;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.18);
  min-width: 160px;
  direction: rtl;
  max-height: calc(100vh - 60px);
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  outline: none;
}

.nav-row {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  height: 32px;
  padding: 0 10px;
  background: none;
  border: none;
  border-radius: 0;
  cursor: pointer;
  text-align: right;
}
.nav-row:hover,
.nav-row--focused {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.nav-row:active {
  background: color-mix(in srgb, var(--text-primary) 10%, transparent);
}

.nav-icon {
  display: flex;
  align-items: center;
  font-size: 18px;
  flex-shrink: 0;
}
.nav-icon svg {
  width: 18px;
  height: 18px;
}
.nav-icon .rtl-flip {
  transform: scaleX(-1);
}

.nav-label {
  font-size: 13px;
  color: var(--text-primary);
  white-space: nowrap;
  flex: 1;
}

.nav-shortcut {
  font-size: 11px;
  color: var(--text-secondary);
  white-space: nowrap;
  direction: ltr;
  margin-inline-start: auto;
}

.nav-divider {
  border: none;
  border-top: 1px solid var(--border-color);
  margin: 0;
}
</style>
