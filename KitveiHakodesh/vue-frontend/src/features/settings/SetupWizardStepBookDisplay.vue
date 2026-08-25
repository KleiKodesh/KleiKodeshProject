<script setup lang="ts">
import { storeToRefs } from 'pinia'
import {
  useSettingsStore,
  DEFAULT_HEADER_FONT_FAMILY,
  DEFAULT_TEXT_FONT_FAMILY,
  DEFAULT_TEAMIM_FONT_FAMILY,
} from '@/stores/settingsStore'
import FontDisplaySettings from './FontDisplaySettings.vue'
import FontPreviewBox from './FontPreviewBox.vue'

// Mishnah, opening of Avot — the sample the book preview renders.
const PREVIEW_HEADING = 'אבות פרק א'
const PREVIEW_BODY =
  'משה קיבל תורה מסיני ומסרה ליהושע ויהושע לזקנים וזקנים לנביאים ונביאים מסרוה לאנשי כנסת הגדולה. הם אמרו שלשה דברים הוו מתונים בדין והעמידו תלמידים הרבה ועשו סייג לתורה.'

const settings = useSettingsStore()
const {
  headerFont,
  textFont,
  teamimTextFont,
  fontSize,
  fontWeight,
  linePadding,
  fixedLineHeight,
} = storeToRefs(settings)
</script>

<template>
  <FontPreviewBox
    :header-font="headerFont"
    :text-font="textFont"
    :font-size="fontSize"
    :font-weight="fontWeight"
    :line-padding="linePadding"
    :heading="PREVIEW_HEADING"
    :body="PREVIEW_BODY"
  />

  <FontDisplaySettings
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
  />
</template>
