<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import HomeTile from './HomePageTile.vue'
import {
  IconLibrary24Filled,
  IconFolder24Filled,
  IconBookOpen24Filled,
  IconApps24Filled,
  IconDatabase24Filled,
  IconArrowDownload24Filled,
} from '@iconify-prerendered/vue-fluent'
import { IconSettings24, IconSearchSparkle24 } from '@iconify-prerendered/vue-fluent-color'
import { isHosted, dbReady } from '@/host/db'
import { useAppNavigation } from '@/composables/useAppNavigation'
import { useTilesKeys } from '@/composables/useTileGridKeys'

const { navigate } = useAppNavigation()

const baseTiles = [
  { label: 'ספרים', icon: IconLibrary24Filled, color: '#B5451B' },
  { label: 'חיפוש', icon: IconSearchSparkle24 },
  { label: 'פתח קובץ', icon: IconFolder24Filled, color: '#f0a500' },
  { label: 'היברו-בוקס', icon: IconBookOpen24Filled, color: '#D94F1E' },
  { label: 'סביבות עבודה', icon: IconApps24Filled, color: '#6b7fc4' },
  { label: 'הגדרות', icon: IconSettings24 },
]

const noDbTiles = [
  { label: 'התקן כזית', icon: IconArrowDownload24Filled, color: '#B5451B' },
  { label: 'בחר מסד נתונים', icon: IconDatabase24Filled, color: '#3478f6' },
  { label: 'פתח קובץ', icon: IconFolder24Filled, color: '#f0a500' },
  { label: 'היברו-בוקס', icon: IconBookOpen24Filled, color: '#D94F1E' },
  { label: 'סביבות עבודה', icon: IconApps24Filled, color: '#6b7fc4' },
  { label: 'הגדרות', icon: IconSettings24 },
]

const tiles = computed(() => (isHosted && !dbReady.value ? noDbTiles : baseTiles))

const pageRef = ref<HTMLElement | null>(null)

const { focusedIndex, containerFocused } = useTilesKeys(
  pageRef,
  () => tiles.value.length,
  (i) => navigate(tiles.value[i].label),
)

onMounted(() => pageRef.value?.focus())

async function onTap(label: string) {
  await navigate(label)
}
</script>

<template>
  <div ref="pageRef" class="home-page" tabindex="0">
    <div class="home-grid">
      <HomeTile
        v-for="(t, i) in tiles"
        :key="t.label"
        v-bind="t"
        :is-focused="containerFocused && focusedIndex === i"
        @tap="onTap(t.label)"
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
  padding-inline: 24px;
  outline: none;
}
.home-grid {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 20px;
}
</style>
