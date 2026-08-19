<script setup lang="ts">
import { ref, computed } from 'vue'
import { onLongPress } from '@vueuse/core'
import { IconArrowRight20Regular, IconArrowLeft20Regular } from '@iconify-prerendered/vue-fluent'
import { useAppShellPane } from '@/composables/useAppShellPane'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { documentIcon, iconKeyForRoute } from '@/utils/documentIcons'

/**
 * One Back/Forward button in the title bar, browser-style: a click steps one
 * frame through the ACTIVE TAB's own history, and press-and-hold opens the
 * whole list in that direction (the MS Edge gesture) — pick any frame to jump
 * straight to it. Greyed out rather than hidden at the ends of the stack, so
 * the buttons beside it never shift as history comes and goes.
 *
 * RTL: back points right, forward left — the direction of reading, matching
 * the Alt+ArrowRight / Alt+ArrowLeft shortcuts in `useAppTitleBarShortcuts`.
 */
const props = defineProps<{
  paneId: 1 | 2
  direction: 'back' | 'forward'
}>()

const pane = useAppShellPane(props.paneId)
const isBack = props.direction === 'back'

const isEnabled = computed(() => (isBack ? pane.canGoBack.value : pane.canGoForward.value))

const HOLD_HINT = 'לחיצה ממושכת מציגה את רשימת ההיסטוריה'
const title = isBack
  ? 'חזור (Alt+חץ ימני)\n' + HOLD_HINT
  : 'קדימה (Alt+חץ שמאלי)\n' + HOLD_HINT

/** The frames on this button's side of the cursor, nearest step first — like
 *  the browser's own hold-to-show list. Each keeps its real entries index so a
 *  pick can jump straight there. Icons come from the shared documentIcons
 *  table, so a frame looks the same here as in the address-bar dropdown. */
const listItems = computed(() => {
  const items = pane.historyEntries.value.map((entry, index) => {
    const icon = documentIcon(iconKeyForRoute(entry.route, entry.isOtzariaAddin))
    return {
      index,
      label: entry.tocPath ? entry.title + ' · ' + entry.tocPath : entry.title,
      iconComponent: icon.icon20,
      iconColor: icon.color,
    }
  })
  const cursor = pane.historyCursor.value
  return isBack ? items.slice(0, cursor).reverse() : items.slice(cursor + 1)
})

const buttonRef = ref<HTMLElement | null>(null)
const dropdownRef = ref<HTMLElement | null>(null)
const isOpen = ref(false)
const dropdownTop = ref(0)
const dropdownLeft = ref(0)
// Set when a long press opened the list, so the click fired on release does
// not also navigate. Cleared on the next press.
const suppressClick = ref(false)

useDropdownClose(dropdownRef, () => (isOpen.value = false), { toggleButton: buttonRef })

onLongPress(
  buttonRef,
  (pressEvent) => {
    // Left button only — a right-button hold would open this underneath the
    // context menu that fires on release.
    if (pressEvent.button !== 0) return
    if (!isEnabled.value || listItems.value.length === 0) return
    suppressClick.value = true
    const rect = buttonRef.value?.getBoundingClientRect()
    if (rect) {
      dropdownTop.value = rect.bottom + 2
      dropdownLeft.value = rect.left
    }
    isOpen.value = true
  },
  { delay: 500 },
)

function onClick() {
  if (suppressClick.value) {
    suppressClick.value = false
    return
  }
  if (isOpen.value) {
    isOpen.value = false
    return
  }
  if (isBack) pane.goBack()
  else pane.goForward()
}

function onSelectItem(index: number) {
  isOpen.value = false
  pane.goToHistoryIndex(index)
}
</script>

<template>
  <button
    ref="buttonRef"
    class="history-button"
    tabindex="-1"
    :disabled="!isEnabled"
    :title="title"
    @pointerdown="suppressClick = false"
    @click.stop="onClick"
  >
    <IconArrowRight20Regular v-if="isBack" />
    <IconArrowLeft20Regular v-else />
  </button>

  <Teleport to="body">
    <div
      v-if="isOpen"
      ref="dropdownRef"
      class="history-button-dropdown"
      :style="{ top: dropdownTop + 'px', left: dropdownLeft + 'px' }"
      @click.stop
    >
      <div
        v-for="item in listItems"
        :key="item.index"
        role="option"
        class="history-button-dropdown-item"
        @click="onSelectItem(item.index)"
      >
        <component
          :is="item.iconComponent"
          class="history-button-dropdown-item-icon"
          :style="item.iconColor ? { color: item.iconColor } : undefined"
        />
        <span class="history-button-dropdown-item-label">{{ item.label }}</span>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
/* Mirrors AppTitleBar's .bar-btn — the parent's scoped styles cannot reach this
   fragment root, so the sizing lives here. Hover/active come from the global
   button rules in main.css. */
.history-button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--title-bar-button-size);
  height: var(--title-bar-button-size);
  padding: 6px;
  border-radius: 4px;
}
.history-button svg {
  width: 16px;
  height: 16px;
}
.history-button:disabled {
  opacity: 0.35;
  cursor: not-allowed;
}
</style>

<style>
/* Unscoped — teleported to <body>, lives outside this component's scope */
.history-button-dropdown {
  position: fixed;
  min-width: 160px;
  max-width: 280px;
  max-height: 320px;
  overflow-y: auto;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.3);
  z-index: 9999;
  padding: 4px 0;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  direction: rtl;
}

.history-button-dropdown-item {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 26px;
  padding: 0 10px;
  font-size: 12px;
  color: var(--text-primary);
  cursor: pointer;
}

.history-button-dropdown-item-icon {
  flex-shrink: 0;
  width: 14px;
  height: 14px;
}

.history-button-dropdown-item-label {
  flex: 1;
  min-width: 0;
  text-align: right;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.history-button-dropdown-item:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
</style>
