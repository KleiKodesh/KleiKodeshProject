<script setup lang="ts">
import { ref } from 'vue'
import { onClickOutside } from '@vueuse/core'
import {
  IconLibrary24Filled,
  IconFolderOpen24Filled,
  IconBookOpen24Filled,
  IconApps24Filled,
} from '@iconify-prerendered/vue-fluent'
import { IconSettings24, IconSearchSparkle24 } from '@iconify-prerendered/vue-fluent-color'
import { useAppNavigation } from '@/composables/useAppNavigation'

const emit = defineEmits<{ close: [] }>()

const { navigate } = useAppNavigation()

const menuRef = ref<HTMLElement | null>(null)
onClickOutside(menuRef, () => emit('close'))

const tiles = [
  { label: 'ספרים', icon: IconLibrary24Filled, color: '#B5451B' },
  { label: 'חיפוש', icon: IconSearchSparkle24, color: undefined },
  { label: 'פתח קובץ', icon: IconFolderOpen24Filled, color: '#f0a500' },
  { label: 'היברו-בוקס', icon: IconBookOpen24Filled, color: '#D94F1E' },
  { label: 'הגדרות', icon: IconSettings24, color: undefined },
  { label: 'סביבות עבודה', icon: IconApps24Filled, color: '#6b7fc4' },
]

async function onTap(label: string) {
  await navigate(label)
  emit('close')
}
</script>

<template>
  <div ref="menuRef" class="nav-dropdown" @click.stop>
    <button v-for="tile in tiles" :key="tile.label" class="nav-row" @click="onTap(tile.label)">
      <span class="nav-icon">
        <component :is="tile.icon" :style="tile.color ? { color: tile.color } : {}" />
      </span>
      <span class="nav-label">{{ tile.label }}</span>
    </button>
  </div>
</template>

<style scoped>
.nav-dropdown {
  position: absolute;
  top: calc(100% + 4px);
  right: 0;
  z-index: 200;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.18);
  min-width: 160px;
  direction: rtl;
  overflow: hidden;
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

.nav-label {
  font-size: 13px;
  color: var(--text-primary);
  white-space: nowrap;
}
</style>
