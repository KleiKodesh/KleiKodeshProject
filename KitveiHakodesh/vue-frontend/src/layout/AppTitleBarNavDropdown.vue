<script setup lang="ts">
import { ref, computed } from 'vue'

import { useDropdownClose } from '@/composables/useDropdownClose'
import {
  IconLibrary24Filled,
  IconFolder24Filled,
  IconBookOpen24Filled,
  IconApps24Filled,
  IconOpen28Regular,
  IconBookLetter24Filled,
  IconRuler24Filled,
  IconCalendarRtl24Filled,
} from '@iconify-prerendered/vue-fluent'
import IconEverythingSearch from '@/components/IconEverythingSearch.vue'
import { IconSettings24, IconSearchSparkle24 } from '@iconify-prerendered/vue-fluent-color'
import { useAppNavigation } from '@/composables/useAppNavigation'
import { showPopOutButton } from '@/webview-host/seforimDb'
import { togglePopOut } from '@/webview-host/bridge'

const emit = defineEmits<{ close: [] }>()

const props = defineProps<{ toggleButtonEl?: HTMLElement | null }>()

const { navigateInNewTab } = useAppNavigation()

const menuRef = ref<HTMLElement | null>(null)
useDropdownClose(menuRef, () => emit('close'), {
  toggleButton: computed(() => props.toggleButtonEl ?? null),
})

const tiles = [
  { label: 'ספרים', icon: IconLibrary24Filled, color: '#B5451B', shortcut: 'Ctrl+1' },
  { label: 'חיפוש', icon: IconSearchSparkle24, color: undefined, shortcut: 'Ctrl+2' },
  { label: 'היברו-בוקס', icon: IconBookOpen24Filled, color: '#D94F1E', shortcut: 'Ctrl+3' },
  { label: 'פתח קובץ', icon: IconFolder24Filled, color: '#f0a500', shortcut: 'Ctrl+4' },
  { label: 'חיפוש קבצים', icon: IconEverythingSearch, color: undefined, shortcut: 'Ctrl+5' },
  { label: 'מילון', icon: IconBookLetter24Filled, color: '#7b5ea7', shortcut: 'Ctrl+6' },
  { label: 'לוח שנה', icon: IconCalendarRtl24Filled, color: '#2e7d32', shortcut: 'Ctrl+7' },
  { label: 'מידות ושיעורים', icon: IconRuler24Filled, color: '#8b6914', shortcut: 'Ctrl+8' },
  { label: 'סביבות עבודה', icon: IconApps24Filled, color: '#6b7fc4', shortcut: 'Ctrl+9' },
  { label: 'הגדרות', icon: IconSettings24, color: undefined, shortcut: 'F1' },
]

async function onTap(label: string) {
  await navigateInNewTab(label)
  emit('close')
}

function onPopOut() {
  togglePopOut()
  emit('close')
}
</script>

<template>
  <div ref="menuRef" class="nav-dropdown" @click.stop>
    <button
      v-for="(tile, index) in tiles"
      :key="tile.label"
      class="nav-row"
      :class="{ 'nav-row-separator': index === tiles.length - 1 }"
      :title="`${tile.label} (${tile.shortcut})`"
      @click="onTap(tile.label)"
    >
      <span class="nav-icon">
        <component :is="tile.icon" :style="tile.color ? { color: tile.color } : {}" />
      </span>
      <span class="nav-label">{{ tile.label }}</span>
      <span class="nav-shortcut">{{ tile.shortcut }}</span>
    </button>
    <button
      v-if="showPopOutButton"
      class="nav-row"
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
}

.nav-row {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  height: 36px;
  padding: 0 10px;
  background: none;
  border: none;
  border-radius: 0;
  cursor: pointer;
  text-align: right;
}
.nav-row:hover {
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

.nav-row-separator {
  border-top: 1px solid var(--border-color);
  margin-top: 3px;
  padding-top: 3px;
  height: 39px;
}
</style>
