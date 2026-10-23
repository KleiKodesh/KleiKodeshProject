<script setup lang="ts">
import { ref, computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useSettingsStore, DEFAULT_HEADER_FONT_FAMILY, DEFAULT_TEXT_FONT_FAMILY, DEFAULT_TEAMIM_FONT_FAMILY } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettings } from './useSettingsPage'
import SettingRow from './SettingRow.vue'
import SliderSetting from './SliderSetting.vue'
import ToggleGroup from './ToggleGroup.vue'
import FontDisplaySettings from './FontDisplaySettings.vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const settings = useSettingsStore()
const {
  resumeLastRead,
  defaultAutoSyncCommentary,
  headerFont,
  textFont,
  teamimTextFont,
  fontSize,
  fontWeight,
  linePadding,
  fixedLineHeight,
  commentaryHeaderFont,
  commentaryTextFont,
  commentaryFontSize,
  commentaryFontWeight,
  commentaryLinePadding,
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
  return value === CONTENT_MAX_WIDTH_UNLIMITED_SENTINEL ? t('settings.book.unlimited') : `${value}px`
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
  <div data-section="section-book-display" :data-section-label="$t('settings.book.section')">
    <div id="section-book-display" class="section-label">{{ $t('settings.book.section') }}</div>

    <SettingRow
      id="nav-resume-last-read"
      :data-nav-label="$t('settings.book.rememberNav')"
      :label="$t('settings.book.remember')"
      :hint="$t('settings.book.rememberHint')"
    >
      <ToggleGroup
        v-model="resumeLastRead"
        :options="[
          { label: $t('settings.yes'), value: true },
          { label: $t('settings.no'), value: false },
        ]"
      />
    </SettingRow>

    <SettingRow id="nav-toolbar-position" :data-nav-label="$t('settings.book.toolbarPosition')" :label="$t('settings.book.toolbarPosition')" wrap>
      <ToggleGroup
        v-model="toolbarPosition"
        :options="[
          { label: $t('settings.book.top'), value: 'top' },
          { label: $t('settings.book.bottom'), value: 'bottom' },
          { label: $t('settings.book.left'), value: 'left' },
          { label: $t('settings.book.right'), value: 'right' },
        ]"
        @update:model-value="bookViewStore.setToolbarPosition($event)"
      />
    </SettingRow>

    <FontDisplaySettings
      id="nav-book-font-display"
      :data-nav-label="$t('settings.book.fontNav')"
      ref="bookDisplayRef"
      v-model:header-font="headerFont"
      v-model:text-font="textFont"
      v-model:teamim-text-font="teamimTextFont"
      v-model:font-size="fontSize"
      v-model:font-weight="fontWeight"
      v-model:line-padding="linePadding"
      v-model:fixed-line-height="fixedLineHeight"
      show-fixed-line-height
      show-teamim-font
      :default-header-font="DEFAULT_HEADER_FONT_FAMILY"
      :default-text-font="DEFAULT_TEXT_FONT_FAMILY"
      :default-teamim-font="DEFAULT_TEAMIM_FONT_FAMILY"
      @close-other="commentaryDisplayRef?.closeDropdowns()"
    />

    <SliderSetting
      id="nav-lines-max-width"
      :data-nav-label="$t('settings.book.maxWidthNav')"
      :label="$t('settings.book.maxWidth')"
      :hint="$t('settings.book.maxWidthHint')"
      v-model="linesContentMaxWidthSlider"
      :min="500"
      :max="950"
      :step="50"
      :format-value="formatMaxWidth"
    />

  </div>

  <!-- ── תצוגת פירושים ── -->
  <div data-section="section-commentary-display" :data-section-label="$t('settings.commentary.section')">
    <div id="section-commentary-display" class="section-label">{{ $t('settings.commentary.section') }}</div>

    <SettingRow
      id="nav-auto-sync-commentary"
      :data-nav-label="$t('settings.commentary.syncNav')"
      :label="$t('settings.commentary.sync')"
      :hint="$t('settings.commentary.syncHint')"
    >
      <ToggleGroup
        v-model="defaultAutoSyncCommentary"
        :options="[
          { label: $t('settings.yes'), value: true },
          { label: $t('settings.no'), value: false },
        ]"
      />
    </SettingRow>

    <FontDisplaySettings
      id="nav-commentary-font-display"
      :data-nav-label="$t('settings.commentary.fontNav')"
      ref="commentaryDisplayRef"
      v-model:header-font="commentaryHeaderFont"
      v-model:text-font="commentaryTextFont"
      v-model:font-size="commentaryFontSize"
      v-model:font-weight="commentaryFontWeight"
      v-model:line-padding="commentaryLinePadding"
      :default-header-font="DEFAULT_HEADER_FONT_FAMILY"
      :default-text-font="DEFAULT_TEXT_FONT_FAMILY"
      @close-other="bookDisplayRef?.closeDropdowns()"
    />

    <SliderSetting
      id="nav-commentary-max-width"
      :data-nav-label="$t('settings.commentary.maxWidthNav')"
      :label="$t('settings.commentary.maxWidth')"
      :hint="$t('settings.book.maxWidthHint')"
      v-model="commentaryMaxWidthSlider"
      :min="500"
      :max="950"
      :step="50"
      :format-value="formatMaxWidth"
    />
  </div>
</template>
