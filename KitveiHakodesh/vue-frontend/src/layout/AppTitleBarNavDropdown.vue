<script setup lang="ts">
import { ref, computed, onMounted, nextTick, inject } from 'vue'

import { useDropdownClose } from '@/composables/useDropdownClose'
import { useListKeys } from '@/composables/useListKeyNav'
import {
  IconOpen28Regular,
  IconChevronDoubleLeft20Regular,
} from '@iconify-prerendered/vue-fluent'
import { APP_NAV_ITEMS, APP_NAV_SETTINGS_ITEM } from './appNavItems'
import { useAppNavUpdateItem } from './appNavUpdateItem'
import { useSettingsStore } from '@/stores/settingsStore'
import { useAppNavigation } from '@/composables/useAppNavigation'
import { showPopOutButton } from '@/webview-host/bridge'
import { togglePopOut } from '@/webview-host/bridge'
import { useEventListener } from '@vueuse/core'

const emit = defineEmits<{ close: [] }>()

const props = defineProps<{ toggleButtonEl?: HTMLElement | null }>()

const { navigateInNewTab } = useAppNavigation()

const menuRef = ref<HTMLElement | null>(null)

const settingsStore = useSettingsStore()

// The rows after the destinations, in render order. Pop-out is hosted-only, so the
// keyboard indexes are read off this list rather than counted by hand.
//
// The sidebar row only ever offers to OPEN the sidebar: while the sidebar is up this menu
// cannot be reached at all (AppTitleBar drops the hamburger and Ctrl+M), and closing it
// again is the rail's own bottom button - or Ctrl+Shift+M, which toggles both ways because
// it returns before toggleNavDropdown's rail-open guard.
//
// Workspaces is deliberately not among them any more: it is a picker, not a destination,
// and it lives in the title bar beside home - one place, not three.
//
// The update row is the Windows / Firefox one: it exists only while an update is
// downloading, ready or installing, sits first after the divider, and a click installs.
// There is no "later" - like Windows' orange dot it simply stays until it is clicked.
type MenuRow = 'update' | 'settings' | 'navSidebar' | 'popOut'

const { item: updateItem, install: installUpdate } = useAppNavUpdateItem()

const menuRows = computed<MenuRow[]>(() => [
  ...(updateItem.value ? ['update' as const] : []),
  'settings',
  'navSidebar',
  ...(showPopOutButton ? ['popOut' as const] : []),
])

useDropdownClose(menuRef, () => emit('close'), {
  toggleButton: computed(() => props.toggleButtonEl ?? null),
})

function menuRowIndex(row: MenuRow) {
  return APP_NAV_ITEMS.value.length + menuRows.value.indexOf(row)
}

const itemCount = computed(() => APP_NAV_ITEMS.value.length + menuRows.value.length)

const { focusedIndex } = useListKeys(menuRef, () => itemCount.value, (index) => {
  activateIndex(index)
})

function activateIndex(index: number) {
  if (index < APP_NAV_ITEMS.value.length) {
    onTap(APP_NAV_ITEMS.value[index]!.label)
    return
  }
  const row = menuRows.value[index - APP_NAV_ITEMS.value.length]
  if (row === 'update') onInstallUpdate()
  else if (row === 'settings') onTap(APP_NAV_SETTINGS_ITEM.label)
  else if (row === 'navSidebar') onShowNavSidebar()
  else if (row === 'popOut') onPopOut()
}

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

// This menu hangs off one pane's title bar, so it opens that pane's rail only.
const paneId = inject<1 | 2>('paneId', 1)

function onShowNavSidebar() {
  settingsStore.setNavSidebarVisible(paneId, true)
  emit('close')
}

function onPopOut() {
  togglePopOut()
  emit('close')
}

// The menu stays open: the row turns into "installing" and, should the installer fail to
// start, into the retry row - both are answers the user is waiting for right here.
function onInstallUpdate() {
  if (updateItem.value?.disabled) return
  void installUpdate()
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
      :title="`${item.text} (${item.shortcut})
${item.description}`"
      @click="onTap(item.label)"
    >
      <span class="nav-icon">
        <component :is="item.icon" :style="item.color ? { color: item.color } : {}" />
      </span>
      <span class="nav-label">{{ item.text }}</span>
      <span class="nav-shortcut">{{ item.shortcut }}</span>
    </button>
    <hr class="nav-divider" />
    <button
      v-if="updateItem"
      class="nav-row nav-row--update"
      :class="{ 'nav-row--focused': focusedIndex === menuRowIndex('update') }"
      :style="{ '--update-color': updateItem.color }"
      :disabled="updateItem.disabled"
      data-nav-item
      aria-live="polite"
      :title="updateItem.title"
      @click="onInstallUpdate"
    >
      <span class="nav-icon"><component :is="updateItem.icon" /></span>
      <span class="nav-label">{{ updateItem.text }}</span>
    </button>
    <button
      class="nav-row"
      :class="{ 'nav-row--focused': focusedIndex === menuRowIndex('settings') }"
      data-nav-item
      :title="`${APP_NAV_SETTINGS_ITEM.text} (${APP_NAV_SETTINGS_ITEM.shortcut})
${APP_NAV_SETTINGS_ITEM.description}`"
      @click="onTap(APP_NAV_SETTINGS_ITEM.label)"
    >
      <span class="nav-icon"><component :is="APP_NAV_SETTINGS_ITEM.icon" /></span>
      <span class="nav-label">{{ APP_NAV_SETTINGS_ITEM.text }}</span>
      <span class="nav-shortcut">{{ APP_NAV_SETTINGS_ITEM.shortcut }}</span>
    </button>
    <button
      class="nav-row"
      :class="{ 'nav-row--focused': focusedIndex === menuRowIndex('navSidebar') }"
      data-nav-item
      :title="$t('layout.showSidebar')"
      @click="onShowNavSidebar"
    >
      <span class="nav-icon"><IconChevronDoubleLeft20Regular /></span>
      <span class="nav-label">{{ $t('layout.sidebar') }}</span>
      <span class="nav-shortcut">Ctrl+Shift+M</span>
    </button>
    <button
      v-if="showPopOutButton"
      class="nav-row"
      :class="{ 'nav-row--focused': focusedIndex === menuRowIndex('popOut') }"
      data-nav-item
      :title="$t('layout.popOutToggle')"
      @click="onPopOut"
    >
      <span class="nav-icon"><IconOpen28Regular /></span>
      <span class="nav-label">{{ $t('layout.popOutLabel') }}</span>
    </button>
  </div>
</template>

<style scoped>
.nav-dropdown {
  position: absolute;
  top: calc(100% + 3px);
  inset-inline-start: 0;
  z-index: 200;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.18);
  min-width: 160px;
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
  text-align: start;
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

/* The row the hamburger's dot points at: the same colour runs down its start edge and
   through its icon, so the dot's reason is obvious the moment the menu opens. */
.nav-row--update {
  border-inline-start: 3px solid var(--update-color);
  padding-inline-start: 7px;
}
.nav-row--update .nav-icon svg {
  color: var(--update-color);
}
.nav-row--update:disabled {
  cursor: default;
}
.nav-row--update:disabled .nav-label {
  color: var(--text-secondary);
}

.nav-divider {
  border: none;
  border-top: 1px solid var(--border-color);
  margin: 0;
}
</style>
