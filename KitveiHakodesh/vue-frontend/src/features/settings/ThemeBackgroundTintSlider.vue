<script setup lang="ts">
import { computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useThemeStore } from '@/theme/themeStore'
import { BACKGROUND_TINT_MIN, BACKGROUND_TINT_MAX, BACKGROUND_TINT_DEFAULT, getTheme, hasBackgroundTint } from '@/theme/themes'
import SliderSetting from './SliderSetting.vue'
import { useI18n } from 'vue-i18n'

// One slider for the settings page and the setup wizard: how emphasized the chosen
// theme's background color is (beige, green...), brightness kept (see tintTheme).
const { backgroundTint, themePreset } = storeToRefs(useThemeStore())

// Hidden for plain white/gray themes — nothing to tint there.
const isTintable = computed(() => {
  const theme = getTheme(themePreset.value)
  return !!theme && hasBackgroundTint(theme)
})

const { t } = useI18n()

function formatTint(value: number): string {
  if (value === BACKGROUND_TINT_DEFAULT) return t('settings.tint.asTheme')
  if (value === BACKGROUND_TINT_MIN) return t('settings.tint.none')
  return `${value}%`
}
</script>

<template>
  <SliderSetting
    v-if="isTintable"
    :label="$t('settings.theme.tintNav')"
    v-model="backgroundTint"
    :min="BACKGROUND_TINT_MIN"
    :max="BACKGROUND_TINT_MAX"
    :step="5"
    :format-value="formatTint"
    :hint="$t('settings.tint.hint')"
  />
</template>
