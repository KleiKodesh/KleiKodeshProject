<script setup lang="ts">
import { ref, computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettings } from './useSettingsPage'
import {
  DIVINE_NAME_MODE_OPTIONS,
  ELOKIM_MODE_OPTIONS,
  OTHER_NAMES_MODE_OPTIONS,
} from '@/utils/censorDivineNames'
import SettingRow from './SettingRow.vue'
import SliderSetting from './SliderSetting.vue'
import ToggleGroup from './ToggleGroup.vue'
import FontDisplaySettings from './FontDisplaySettings.vue'

const settings = useSettingsStore()
const {
  divineNameMode,
  elokimMode,
  otherNamesMode,
  defaultAutoSyncCommentary,
  headerFont,
  textFont,
  fontSize,
  linePadding,
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
  <!-- ── קריאה ── -->
  <div data-section="section-reading" data-section-label="קריאה">
    <div id="section-reading" class="section-label">קריאה</div>

    <SettingRow
      id="nav-auto-sync-commentary"
      data-nav-label="סנכרן מפרשים"
      label="סנכרן מפרשים כברירת מחדל"
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

    <SettingRow
      id="nav-censor-divine-names"
      data-nav-label="כיסוי שם ה'"
      label="כיסוי שם ה' המפורש"
      hint="בחר כיצד ייכתב שם ה' המפורש. 'כתיב מלא' מבטל את הכיסוי בכל שמות הקודש"
      wrap
    >
      <ToggleGroup v-model="divineNameMode" :options="[...DIVINE_NAME_MODE_OPTIONS]" />
    </SettingRow>

    <SettingRow
      v-if="divineNameMode !== 'none'"
      id="nav-censor-elokim"
      data-nav-label="כיסוי אלהים"
      label="כיסוי שם אלהים"
      hint="חל גם על אלוהים, אלהי ואלוה. בהחלפת אות הנקודות והטעמים נשמרים במקומם"
      wrap
    >
      <ToggleGroup v-model="elokimMode" :options="[...ELOKIM_MODE_OPTIONS]" />
    </SettingRow>

    <SettingRow
      v-if="divineNameMode !== 'none'"
      id="nav-censor-other-names"
      data-nav-label="כיסוי אדני, אל, שדי"
      label="כיסוי שאר שמות הקודש"
      hint="אדנ‑י, א‑ל, ש‑די — שמות שאין בהם אות ה׳ להחלפה, ולכן ניתן רק להפרידם או להשאירם בכתיב מלא"
      wrap
    >
      <ToggleGroup v-model="otherNamesMode" :options="[...OTHER_NAMES_MODE_OPTIONS]" />
    </SettingRow>
  </div>

  <!-- ── תצוגת ספר + תצוגת פירושים ── -->
  <div data-section="section-book-display" data-section-label="תצוגת ספר">
    <div id="section-book-display" class="section-label">תצוגת ספר</div>

    <SettingRow id="nav-toolbar-position" data-nav-label="מיקום סרגל הכלים" label="מיקום סרגל הכלים בתצוגת ספר" wrap>
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
      @close-other="commentaryDisplayRef?.closeDropdowns()"
    />

    <SliderSetting
      id="nav-lines-max-width"
      data-nav-label="רוחב מקסימלי"
      label="רוחב מקסימלי עבור עמודת הטקסט"
      hint="הגבל את רוחב שורת הקריאה לנוחות מרבית"
      v-model="linesContentMaxWidthSlider"
      :min="500"
      :max="950"
      :step="50"
      :format-value="formatMaxWidth"
    />

    <div id="section-commentary-display" class="subsection-label">תצוגת פירושים</div>

    <SettingRow id="nav-commentary-settings-mode" data-nav-label="הגדרות נפרדות לפירושים" hint="האם להשתמש בהגדרות גופן נפרדות לפירושים, או לרשת את הגדרות הספר">
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
      label="רוחב מקסימלי עבור עמודת הפירושים"
      hint="הגבל את רוחב שורת הקריאה בפירושים לנוחות מרבית"
      v-model="commentaryMaxWidthSlider"
      :min="500"
      :max="950"
      :step="50"
      :format-value="formatMaxWidth"
    />
  </div>
</template>
