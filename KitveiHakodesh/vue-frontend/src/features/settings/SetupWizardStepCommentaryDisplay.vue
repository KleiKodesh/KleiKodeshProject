<script setup lang="ts">
import { storeToRefs } from 'pinia'
import {
  useSettingsStore,
  DEFAULT_HEADER_FONT_FAMILY,
  DEFAULT_TEXT_FONT_FAMILY,
} from '@/stores/settingsStore'
import SettingRow from './SettingRow.vue'
import ToggleGroup from './ToggleGroup.vue'
import FontDisplaySettings from './FontDisplaySettings.vue'
import FontPreviewBox from './FontPreviewBox.vue'

// Rashi on the first verse of Genesis — a commentary sample for a commentary preview.
const PREVIEW_HEADING = 'רש"י בראשית א׳ א׳'
const PREVIEW_BODY =
  'בראשית ברא. אין המקרא הזה אומר אלא דרשני, כמו שדרשו רבותינו זכרונם לברכה: בשביל התורה שנקראת ראשית דרכו, ובשביל ישראל שנקראו ראשית תבואתו.'

const settings = useSettingsStore()
const {
  commentaryHeaderFont,
  commentaryTextFont,
  commentaryFontSize,
  commentaryFontWeight,
  commentaryLinePadding,
  useSeparateCommentarySettings,
} = storeToRefs(settings)
</script>

<template>
  <!-- The commentary fields mirror the book's while 'same as book' is selected — the
       mirror watcher lives in SetupWizard.vue, which stays mounted across steps — so
       these bind straight through and show the truth in both modes. -->
  <FontPreviewBox
    :header-font="commentaryHeaderFont"
    :text-font="commentaryTextFont"
    :font-size="commentaryFontSize"
    :font-weight="commentaryFontWeight"
    :line-padding="commentaryLinePadding"
    :heading="PREVIEW_HEADING"
    :body="PREVIEW_BODY"
  />

  <SettingRow
    label="הגדרות תצוגת הפירושים"
    hint="'זהה לתצוגת ספר' מחיל על הפירושים את הגדרות הספר"
    wrap
  >
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
    v-model:header-font="commentaryHeaderFont"
    v-model:text-font="commentaryTextFont"
    v-model:font-size="commentaryFontSize"
    v-model:font-weight="commentaryFontWeight"
    v-model:line-padding="commentaryLinePadding"
    :default-header-font="DEFAULT_HEADER_FONT_FAMILY"
    :default-text-font="DEFAULT_TEXT_FONT_FAMILY"
  />
</template>
