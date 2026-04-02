<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import HomeTile from './HomePageTile.vue'
import {
  IconLibrary24Filled,
  IconFolderOpen24Filled,
  IconBookOpen24Filled,
  IconApps24Filled,
  IconDatabase24Filled,
  IconArrowDownload24Filled,
} from '@iconify-prerendered/vue-fluent'
import { IconSettings24, IconSearchSparkle24 } from '@iconify-prerendered/vue-fluent-color'
import { useTabStore } from '@/stores/tabStore'
import { useGridLayout } from '@/composables/useGridLayout'
import { isHosted, dbReady } from '@/host/db'
import { useEventListener } from '@vueuse/core'
import { useAppNavigation } from '@/composables/useAppNavigation'

const tabStore = useTabStore()
const { navigate } = useAppNavigation()

const baseTiles = [
  { label: 'ספרים', icon: IconLibrary24Filled, color: '#B5451B' },
  { label: 'חיפוש', icon: IconSearchSparkle24 },
  { label: 'פתח קובץ', icon: IconFolderOpen24Filled, color: '#f0a500' },
  { label: 'היברו-בוקס', icon: IconBookOpen24Filled, color: '#D94F1E' },
  { label: 'הגדרות', icon: IconSettings24 },
  { label: 'סביבות עבודה', icon: IconApps24Filled, color: '#6b7fc4' },
]

const noDbTiles = [
  { label: 'התקן זית', icon: IconArrowDownload24Filled, color: '#B5451B' },
  { label: 'בחר מסד נתונים', icon: IconDatabase24Filled, color: '#3478f6' },
  { label: 'פתח קובץ', icon: IconFolderOpen24Filled, color: '#f0a500' },
  { label: 'היברו-בוקס', icon: IconBookOpen24Filled, color: '#D94F1E' },
  { label: 'הגדרות', icon: IconSettings24 },
  { label: 'סביבות עבודה', icon: IconApps24Filled, color: '#6b7fc4' },
]

const tiles = computed(() => (isHosted && !dbReady.value ? noDbTiles : baseTiles))

const pageRef = ref<HTMLElement | null>(null)
const gridRef = ref<HTMLElement | null>(null)
const tileCount = computed(() => tiles.value.length)
const { cols } = useGridLayout(pageRef, tileCount)

const tileRefs = ref<InstanceType<typeof HomeTile>[]>([])
const focusedIndex = ref<number | null>(null)

function focusTile(index: number) {
  const clamped = Math.max(0, Math.min(index, tiles.value.length - 1))
  focusedIndex.value = clamped
  const el = tileRefs.value[clamped]?.$el as HTMLElement | undefined
  el?.focus()
}

useEventListener(pageRef, 'keydown', (e: KeyboardEvent) => {
  const arrows = ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight']
  if (!arrows.includes(e.key)) return
  e.preventDefault()

  if (focusedIndex.value === null) {
    focusTile(0)
    return
  }

  const c = cols.value
  const current = focusedIndex.value
  // RTL: ArrowRight moves to previous index (toward start), ArrowLeft moves to next
  if (e.key === 'ArrowDown') focusTile(current + c)
  else if (e.key === 'ArrowUp') focusTile(current - c)
  else if (e.key === 'ArrowLeft') focusTile(current + 1)
  else if (e.key === 'ArrowRight') focusTile(current - 1)
})

useEventListener(pageRef, 'focusout', (e: FocusEvent) => {
  if (!pageRef.value?.contains(e.relatedTarget as Node)) {
    focusedIndex.value = null
  }
})

onMounted(() => pageRef.value?.focus())

async function onTap(label: string) {
  await navigate(label)
}
</script>

<template>
  <div ref="pageRef" class="home-page" tabindex="0">
    <div
      ref="gridRef"
      class="home-grid tiles-grid"
      :style="{ gridTemplateColumns: `repeat(${cols}, 1fr)` }"
    >
      <HomeTile
        v-for="(t, i) in tiles"
        :key="t.label"
        ref="tileRefs"
        v-bind="t"
        :is-focused="focusedIndex === i"
        @tap="onTap(t.label)"
        @focus="focusedIndex = i"
      />
    </div>
  </div>
</template>

<style scoped>
.home-page {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  outline: none;
}
.home-grid {
  display: grid;
  gap: 16px;
}
</style>
