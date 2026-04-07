<script setup lang="ts">
import { ref } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import SettingRow from './SettingRow.vue'
import ToggleGroup from './ToggleGroup.vue'
import FontDisplaySettings from './FontDisplaySettings.vue'

const settings = useSettingsStore()
const {
  headerFont,
  textFont,
  fontSize,
  linePadding,
  commentaryHeaderFont,
  commentaryTextFont,
  commentaryFontSize,
  commentaryLinePadding,
  useSeparateCommentarySettings,
} = storeToRefs(settings)

const bookDisplayRef = ref<InstanceType<typeof FontDisplaySettings> | null>(null)
const commentaryDisplayRef = ref<InstanceType<typeof FontDisplaySettings> | null>(null)
</script>

<template>
  <div class="reading-pane">
    <div class="section-header">תצוגת ספר</div>

    <FontDisplaySettings
      ref="bookDisplayRef"
      v-model:header-font="headerFont"
      v-model:text-font="textFont"
      v-model:font-size="fontSize"
      v-model:line-padding="linePadding"
      @close-other="commentaryDisplayRef?.closeDropdowns()"
    />

    <div class="section-header">תצוגת פירושים</div>

    <SettingRow>
      <ToggleGroup
        v-model="useSeparateCommentarySettings"
        :options="[
          { label: 'זהה לתצוגת ספר', value: false },
          { label: 'הגדרות נפרדות', value: true },
        ]"
      />
    </SettingRow>

    <FontDisplaySettings
      v-if="useSeparateCommentarySettings"
      ref="commentaryDisplayRef"
      v-model:header-font="commentaryHeaderFont"
      v-model:text-font="commentaryTextFont"
      v-model:font-size="commentaryFontSize"
      v-model:line-padding="commentaryLinePadding"
      @close-other="bookDisplayRef?.closeDropdowns()"
    />
  </div>
</template>

<style scoped>
.reading-pane {
  display: contents;
}
.section-header {
  font-size: 12px;
  font-weight: 700;
  color: var(--text-primary);
  padding: 4px 0;
  margin-top: 16px;
  margin-bottom: 10px;
  border-bottom: 1px solid var(--border-color);
}
</style>
