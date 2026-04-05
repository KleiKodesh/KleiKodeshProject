<script setup lang="ts">
import { computed } from 'vue'
import { storeToRefs } from 'pinia'
import {
  IconSearch20Regular,
  IconLayoutRowTwo20Regular,
  IconLayoutRowTwoFocusBottom20Filled,
  IconZoomIn20Regular,
  IconZoomOut20Regular,
} from '@iconify-prerendered/vue-fluent'
import IconTreeRtl from '@/components/common/IconTreeRtl.vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { ZOOM_CONFIG } from '@/composables/useZoom'

defineProps<{ bottomVisible: boolean; searchVisible: boolean; tocVisible: boolean }>()
defineEmits<{ toggleBottom: []; toggleSearch: []; toggleToc: [] }>()

const settingsStore = useSettingsStore()
const bookViewStore = useBookViewStore()
const { zoom } = storeToRefs(bookViewStore)
const diacriticsState = computed(() => settingsStore.diacriticsState)
const diacriticsTitle = computed(
  () => ['הסר טעמים', 'הסר גם ניקוד', 'שחזר טעמים וניקוד'][diacriticsState.value]!,
)
</script>

<template>
  <div class="book-view-toolbar">
    <button :class="{ active: searchVisible }" title="חיפוש" @click="$emit('toggleSearch')">
      <IconSearch20Regular />
    </button>
    <button :class="{ active: tocVisible }" title="תוכן עניינים" @click="$emit('toggleToc')">
      <IconTreeRtl />
    </button>
    <button
      :class="{ active: bottomVisible }"
      title="פאנל תחתון (Ctrl+J)"
      @click="$emit('toggleBottom')"
    >
      <IconLayoutRowTwoFocusBottom20Filled v-if="bottomVisible" /><IconLayoutRowTwo20Regular
        v-else
      />
    </button>
    <button
      :title="`הקטן (Ctrl-)\nזום: ${zoom}%\nאיפוס: Ctrl+0`"
      :disabled="zoom <= ZOOM_CONFIG.MIN"
      @click="bookViewStore.zoomOut()"
    >
      <IconZoomOut20Regular />
    </button>
    <button
      :title="`הגדל (Ctrl+)\nזום: ${zoom}%\nאיפוס: Ctrl+0`"
      :disabled="zoom >= ZOOM_CONFIG.MAX"
      @click="bookViewStore.zoomIn()"
    >
      <IconZoomIn20Regular />
    </button>
    <button
      :class="[
        'diacritics-btn',
        { 'state-1': diacriticsState === 1, 'state-2': diacriticsState === 2 },
      ]"
      :title="diacriticsTitle"
      @click="settingsStore.cycleDiacritics()"
    >
      <svg
        v-if="diacriticsState === 0"
        width="16"
        height="18"
        viewBox="0 0 126 139"
        fill="currentColor"
      >
        <g transform="translate(0,139) scale(0.1,-0.1)">
          <path
            d="M398 1153c-37-40-48-66-48-112 0-56 15-90 62-138 39-40 40-41 19-52-28-15-68-87-76-137-3-22-1-70 5-106 13-71 4-108-25-108-8 0-15-7-15-15 0-12 19-15 113-15 134 0 157 10 157 68 0 42-12 62-82 141-51 59-61 99-34 136 13 18 24 9 180-139 134-128 167-164 172-192 8-45 27-43 63 6 59 81 49 150-34 242-49 54-57 90-33 154 9 27 17 33 50 37 22 3 42 7 44 10 3 3 5 32 5 66 1 94-27 126-118 137-26 3-61 15-76 26-39 27-50 14-55-69-5-80 13-122 64-149 24-13 32-23 28-34-4-8-9-25-11-37-3-13-9-23-13-23-8 0-232 208-267 249-12 14-25 38-28 53-8 35-15 35-47 1z"
          />
          <path
            d="M450 410c0 27 3 30 30 30s30-3 30-33c0-26 6-36 24-45 37-16 57-54 63-116l6-56H566c-36 0-36 0-36 43 0 113-91 116-106 4-6-44-9-47-35-47-28 0-29 2-29 50 0 63 13 89 55 118 27 17 35 29 35 52Z"
          />
          <path
            d="M650 395V360h50c47 0 50-2 50-25 0-14-4-25-9-25-19 0-23-41-7-65 22-33 60-33 82 0 16 24 12 65-7 65-5 0-9 11-9 25 0 23 3 25 50 25h50v35 35H775 650V395Z"
          />
        </g>
      </svg>
      <svg
        v-else-if="diacriticsState === 1"
        width="16"
        height="18"
        viewBox="0 0 112 135"
        fill="currentColor"
      >
        <g transform="translate(0,135) scale(0.1,-0.1)">
          <path
            d="M328 1103c-37-40-48-66-48-112 0-56 15-90 62-138 39-40 40-41 19-52-28-15-68-87-76-137-3-22-1-70 5-106 13-71 4-108-25-108-8 0-15-7-15-15 0-12 19-15 113-15 134 0 157 10 157 68 0 42-12 62-82 141-51 59-61 99-34 136 13 18 24 9 180-139 134-128 167-164 172-192 8-45 27-43 63 6 59 81 49 150-34 242-49 54-57 90-33 154 9 27 17 33 50 37 22 3 42 7 44 10 3 3 5 32 5 66 1 94-27 126-118 137-26 3-61 15-76 26-39 27-50 14-55-69-5-80 13-122 64-149 24-13 32-23 28-34-4-8-9-25-11-37-3-13-9-23-13-23-8 0-232 208-267 249-12 14-25 38-28 53-8 35-15 35-47 1z"
          />
          <path
            d="M440 345l0-35 50 0c47 0 50-2 50-25 0-14-4-25-9-25-17 0-20-45-5-67 30-46 104-12 90 42-4 14-11 25-16 25-6 0-10 11-10 25 0 23 3 25 50 25l50 0 0 35 0 35-125 0-125 0 0-35z"
          />
        </g>
      </svg>
      <svg
        v-else
        width="16"
        height="16"
        viewBox="0 0 88 111"
        fill="currentColor"
        style="transform: scale(0.85)"
      >
        <g transform="translate(0,111) scale(0.1,-0.1)">
          <path
            d="M198 903c-37-40-48-66-48-112 0-56 15-90 62-138 39-40 40-41 19-52-28-15-68-87-76-137-3-22-1-70 5-106 13-71 4-108-25-108-8 0-15-7-15-15 0-12 19-15 113-15 134 0 157 10 157 68 0 42-12 62-82 141-51 59-61 99-34 136 13 18 24 9 180-139 134-128 167-164 172-192 8-45 27-43 63 6 59 81 49 150-34 242-49 54-57 90-33 154 9 27 17 33 50 37 22 3 42 7 44 10 3 3 5 32 5 66 1 94-27 126-118 137-26 3-61 15-76 26-39 27-50 14-55-69-5-80 13-122 64-149 24-13 32-23 28-34-4-8-9-25-11-37-3-13-9-23-13-23-8 0-232 208-267 249-12 14-25 38-28 53-8 35-15 35-47 1z"
          />
        </g>
      </svg>
    </button>
  </div>
</template>

<style scoped>
.book-view-toolbar {
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0;
  padding-inline: 4px;
  background: var(--bg-toolbar);
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
  transition: background 120ms;
}
button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  padding: 6px;
  border-radius: 4px;
}
button svg {
  width: 16px;
  height: 16px;
}
button.active {
  color: var(--accent-color);
}
.diacritics-btn.state-1 {
  color: #ff8c00;
}
.diacritics-btn.state-2 {
  color: #ff4500;
}
.rtl-flip {
  transform: scaleX(-1);
}
</style>
