<script setup lang="ts">
import { ref, computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettings } from './useSettingsPage'
import SettingRow from './SettingRow.vue'
import SliderSetting from './SliderSetting.vue'
import ToggleGroup from './ToggleGroup.vue'
import FontDisplaySettings from './FontDisplaySettings.vue'

const settings = useSettingsStore()
const {
  resumeLastRead,
  defaultAutoSyncCommentary,
  headerFont,
  textFont,
  fontSize,
  linePadding,
  fixedLineHeight,
  commentaryHeaderFont,
  commentaryTextFont,
  commentaryFontSize,
  commentaryLinePadding,
  useSeparateCommentarySettings,
  linesContentMaxWidth,
  commentaryMaxWidth,
} = storeToRefs(settings)

useSettings() // wires the commentary-mirror watcher

const bookViewStore = useBookViewStore()
const { toolbarPosition } = storeToRefs(bookViewStore)

const bookDisplayRef = ref<InstanceType<typeof FontDisplaySettings> | null>(null)
const commentaryDisplayRef = ref<InstanceType<typeof FontDisplaySettings> | null>(null)

// 950 is the sentinel "ללא הגבלה" stop at the top of the slider.
// Stored value is 0 (unlimited) or 400–900 (px). The slider uses 400–950 step 50.
const CONTENT_MAX_WIDTH_UNLIMITED_SENTINEL = 950

function formatMaxWidth(value: number): string {
  return value === CONTENT_MAX_WIDTH_UNLIMITED_SENTINEL ? 'ללא הגבלה' : `${value}px`
}

const linesContentMaxWidthSlider = computed({
  get: () => linesContentMaxWidth.value === 0 ? CONTENT_MAX_WIDTH_UNLIMITED_SENTINEL : linesContentMaxWidth.value,
  set: (sliderValue: number) => {
    linesContentMaxWidth.value = sliderValue === CONTENT_MAX_WIDTH_UNLIMITED_SENTINEL ? 0 : sliderValue
  },
})

const commentaryMaxWidthSlider = computed({
  get: () => commentaryMaxWidth.value === 0 ? CONTENT_MAX_WIDTH_UNLIMITED_SENTINEL : commentaryMaxWidth.value,
  set: (sliderValue: number) => {
    commentaryMaxWidth.value = sliderValue === CONTENT_MAX_WIDTH_UNLIMITED_SENTINEL ? 0 : sliderValue
  },
})
</script>

<template>
  <!-- ── תצוגת ספר ── -->
  <div data-section="section-book-display" data-section-label="תצוגת ספר">
    <div id="section-book-display" class="section-label">תצוגת ספר</div>

    <SettingRow
      id="nav-resume-last-read"
      data-nav-label="זכור מיקום אחרון"
      label="זכור מיקום אחרון בספר"
      hint="בפתיחת ספר מחדש, האפליקציה תחזור אוטומטית למקום שבו הפסקת לקרוא"
    >
      <ToggleGroup
        v-model="resumeLastRead"
        :options="[
          { label: 'כן', value: true },
          { label: 'לא', value: false },
        ]"
      />
    </SettingRow>

    <SettingRow id="nav-toolbar-position" data-nav-label="מיקום סרגל הכלים" label="מיקום סרגל הכלים" wrap>
      <ToggleGroup
        v-model="toolbarPosition"
        :options="[
          { label: 'למעלה', value: 'top' },
          { label: 'למטה', value: 'bottom' },
          { label: 'שמאל', value: 'left' },
          { label: 'ימין', value: 'right' },
        ]"
        @update:model-value="bookViewStore.setToolbarPosition($event)"
      />
    </SettingRow>

    <FontDisplaySettings
      id="nav-book-font-display"
      data-nav-label="גופן וגודל"
      ref="bookDisplayRef"
      v-model:header-font="headerFont"
      v-model:text-font="textFont"
      v-model:font-size="fontSize"
      v-model:line-padding="linePadding"
      v-model:fixed-line-height="fixedLineHeight"
      show-fixed-line-height
      @close-other="commentaryDisplayRef?.closeDropdowns()"
    />

    <SliderSetting
      id="nav-lines-max-width"
      data-nav-label="רוחב מקסימלי"
      label="רוחב מקסימלי לעמודת הטקסט"
      hint="רוחב שורה קצר יותר נוח יותר לקריאה"
      v-model="linesContentMaxWidthSlider"
      :min="500"
      :max="950"
      :step="50"
      :format-value="formatMaxWidth"
    />

  </div>

  <!-- ── תצוגת פירושים ── -->
  <div data-section="section-commentary-display" data-section-label="תצוגת פירושים">
    <div id="section-commentary-display" class="section-label">תצוגת פירושים</div>

    <SettingRow
      id="nav-auto-sync-commentary"
      data-nav-label="סנכרן מפרשים"
      label="סנכרן מפרשים כברירת מחדל בפתיחת ספר"
      hint="ניתן לשנות לכל ספר בנפרד דרך כפתור סנכרן מפרשים בסרגל הכלים"
    >
      <ToggleGroup
        v-model="defaultAutoSyncCommentary"
        :options="[
          { label: 'כן', value: true },
          { label: 'לא', value: false },
        ]"
      />
    </SettingRow>

    <SettingRow id="nav-commentary-settings-mode" data-nav-label="הגדרות נפרדות לפירושים" hint="'זהה לתצוגת ספר' מחיל על הפירושים את הגדרות הספר">
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
      id="nav-commentary-font-display"
      data-nav-label="גופן פירושים"
      ref="commentaryDisplayRef"
      v-model:header-font="commentaryHeaderFont"
      v-model:text-font="commentaryTextFont"
      v-model:font-size="commentaryFontSize"
      v-model:line-padding="commentaryLinePadding"
      @close-other="bookDisplayRef?.closeDropdowns()"
    />

    <SliderSetting
      v-if="useSeparateCommentarySettings"
      id="nav-commentary-max-width"
      data-nav-label="רוחב מקסימלי פירושים"
      label="רוחב מקסימלי לעמודת הפירושים"
      hint="רוחב שורה קצר יותר נוח יותר לקריאה"
      v-model="commentaryMaxWidthSlider"
      :min="500"
      :max="950"
      :step="50"
      :format-value="formatMaxWidth"
    />
  </div>
</template>
